import argparse
import os
import re
import socket
import sys
import tempfile
import threading
import time
from pathlib import Path

from flask import Flask, jsonify, request
from flask_cors import CORS

DEFAULT_DATASET = Path(__file__).parent / "dataset"
DEFAULT_PORT = 5001
BACKEND_PORT = 5233

MODEL_NAME = "Facenet"
DETECTOR = "opencv"
DISTANCE_METRIC = "cosine"

_SECONDS_PER_IMAGE = 0.8

_dataset_path: str | None = None

_deepface = None

_model_ready = False

_embeddings_ready = False

_embeddings_total: int = 0

_warmup_error: str | None = None

_warmup_start: float = 0.0

_inference_lock = threading.Lock()

app = Flask(__name__)
CORS(app)

def _get_pkl_path(dataset_path: str) -> Path:
    name = f"ds_model_{MODEL_NAME}_detector_{DETECTOR}_aligned_True.pkl"
    return Path(dataset_path) / name

def _estimate_build_minutes(n_images: int) -> int:
    return max(1, round(n_images * _SECONDS_PER_IMAGE / 60))

def _check_pkl(dataset_path: str, img_count: int) -> tuple[bool, int]:
    exists = _get_pkl_path(dataset_path).exists()
    est = _estimate_build_minutes(img_count) if not exists and img_count > 0 else 0
    return exists, est

def _warmup() -> None:
    global _deepface, _model_ready, _warmup_error, _embeddings_ready, _embeddings_total

    try:
        print(" [Warmup] Wczytywanie TensorFlow i modelu Facenet…")
        print(" (pierwsze uruchomienie może potrwać kilkanaście sekund – pobieranie wag ~93 MB)")
        t0 = time.time()

        from deepface import DeepFace

        DeepFace.build_model(MODEL_NAME)
        _deepface = DeepFace

        elapsed = time.time() - t0
        print(f"[Warmup] Model wczytany ({elapsed:.1f} s)")

        _model_ready = True

        if _dataset_path and Path(_dataset_path).exists():
            imgs = sorted(
                f for f in Path(_dataset_path).rglob("*")
                if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"}
            )
            _embeddings_total = len(imgs)

            if not imgs:
                print("ℹ  [Embeddings] Dataset jest pusty – embeddingi zostaną zbudowane po dodaniu zdjęć.")
                _embeddings_ready = True
            else:
                pkl = _get_pkl_path(_dataset_path)
                pkl_exists, est = _check_pkl(_dataset_path, _embeddings_total)

                if pkl_exists:
                    print(f" [Embeddings] Wczytano bazę embeddingów z cache ({pkl.name})")
                    _embeddings_ready = True
                else:
                    print(f"[Embeddings] Budowanie bazy embeddingów ({_embeddings_total} zdjęć)…")
                    print(f"   Szacowany czas: ~{est} min. Wynik zostanie zapisany jako {pkl.name}.")

                    if _embeddings_total > 200:
                        print(
                            f"\n   ⚠  Duży dataset ({_embeddings_total} zdjęć). Następnym razem"
                            f" zbuduj bazę z wyprzedzeniem:\n      python service.py --build-db\n"
                            f"   i pozostaw działający w tle."
                            f" Po zakończeniu każde kolejne uruchomienie zajmie ~30 s.\n"
                        )

                    t1 = time.time()
                    try:
                        DeepFace.find(
                            img_path=str(imgs[0]),
                            db_path=str(_dataset_path),
                            model_name=MODEL_NAME,
                            distance_metric=DISTANCE_METRIC,
                            detector_backend=DETECTOR,
                            enforce_detection=False,
                            align=True,
                            silent=True,
                        )
                        elapsed2 = time.time() - t1
                        print(f"[Embeddings] Baza embeddingów gotowa ({elapsed2:.1f} s)")
                    except Exception as emb_err:
                        print(f"[Embeddings] Nie udało się wstępnie zbudować embeddingów: {emb_err}")

                    _embeddings_ready = True
        else:
            print("ℹ  [Warmup] Brak datasetu – embeddingi zostaną zbudowane po uruchomieniu z --dataset.")
            _embeddings_ready = True

        total = time.time() - _warmup_start
        print(f"\n[Warmup] Serwis gotowy do pracy! (łączny czas: {total:.1f} s)\n")

    except Exception as exc:
        _warmup_error = str(exc)
        print(f"\n[Warmup] Błąd podczas wczytywania modelu: {exc}")
        print("   Serwis nie będzie mógł rozpoznawać twarzy. Sprawdź instalację zależności.\n")

@app.route("/health", methods=["GET"])
def health():
    dataset_ok = _dataset_path is not None and Path(_dataset_path).exists()
    count = 0
    if dataset_ok:
        count = sum(1 for f in Path(_dataset_path).rglob("*")
                    if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"})

    if not _model_ready:
        status = "error" if _warmup_error else "loading"
    elif not _embeddings_ready:
        status = "building_embeddings"
    else:
        status = "ok"

    elapsed = round(time.time() - _warmup_start, 1) if _warmup_start else 0

    return jsonify({
        "status": status,
        "model_ready": _model_ready,
        "embeddings_ready": _embeddings_ready,
        "embeddings_total": _embeddings_total,
        "warmup_error": _warmup_error,
        "warmup_elapsed_s": elapsed,
        "dataset": str(_dataset_path),
        "dataset_exists": dataset_ok,
        "images_in_dataset": count,
        "model": MODEL_NAME,
    })

@app.route("/recognize", methods=["POST"])
def recognize():
    if not _model_ready:
        if _warmup_error:
            return jsonify({
                "error": f"Model nie mógł zostać wczytany: {_warmup_error}. Sprawdź logi serwisu."
            }), 503
        elapsed = round(time.time() - _warmup_start, 1) if _warmup_start else 0
        return jsonify({
            "error": f"Serwis się jeszcze uruchamia (wczytywanie modelu, {elapsed} s)."
                     " Poczekaj chwilę i spróbuj ponownie."
        }), 503

    if not _embeddings_ready:
        elapsed = round(time.time() - _warmup_start, 1) if _warmup_start else 0
        est = _estimate_build_minutes(_embeddings_total) if _embeddings_total > 0 else 1
        tip = (
            f" Dla dużego datasetu ({_embeddings_total} zdjęć) uruchom wcześniej:"
            f" python service.py --build-db i pozostaw działający w tle."
            f" Kolejne starty serwisu będą szybkie (~30 s)."
            if _embeddings_total > 200 else ""
        )
        return jsonify({
            "error": (
                f"Baza embeddingów jest jeszcze budowana"
                f" ({elapsed} s, szacowany czas: ~{est} min).{tip}"
            )
        }), 503

    if _dataset_path is None or not Path(_dataset_path).exists():
        return jsonify({
            "error": f"Folder z datasetem nie istnieje: {_dataset_path}. "
                     "Utwórz folder dataset/ i umieść w nim zdjęcia twarzy."
        }), 503

    if "image" not in request.files:
        return jsonify({"error": "Brakuje pola 'image' w formularzu."}), 400

    file = request.files["image"]
    if file.filename == "":
        return jsonify({"error": "Przesłano pusty plik."}), 400

    suffix = Path(file.filename).suffix or ".jpg"
    tmp_path = None
    try:
        with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as tmp:
            file.save(tmp.name)
            tmp_path = tmp.name

        with _inference_lock:
            results = _deepface.find(
                img_path=tmp_path,
                db_path=str(_dataset_path),
                model_name=MODEL_NAME,
                distance_metric=DISTANCE_METRIC,
                detector_backend=DETECTOR,
                enforce_detection=False,
                align=True,
                silent=True,
            )

        if not results or results[0].empty:
            return jsonify({"label": "", "confidence": 0.0})

        df = results[0].sort_values("distance")
        best = df.iloc[0]

        label = Path(str(best["identity"])).name

        raw_confidence = float(best.get("confidence", 0))
        confidence = round(raw_confidence / 100.0, 4)

        return jsonify({"label": label, "confidence": confidence})

    except Exception as exc:
        msg = str(exc)
        return jsonify({"error": f"Błąd rozpoznawania: {msg}"}), 500
    finally:
        if tmp_path:
            try:
                os.unlink(tmp_path)
            except Exception:
                pass

@app.route("/add-person", methods=["POST"])
def add_person():
    global _embeddings_ready

    if _dataset_path is None:
        return jsonify({"error": "sciezka dataset nieskonfigurowana uzyj --dataset."}), 503

    name = (request.form.get("name") or "").strip()
    if not name:
        return jsonify({"error": "Brakuje lub jest puste pole 'name'."}), 400

    if "image" not in request.files:
        return jsonify({"error": "Brakuje pola 'image' w formularzu."}), 400

    file = request.files["image"]
    if not file.filename:
        return jsonify({"error": "Brakuje pliku."}), 400

    safe_name = re.sub(r'[/\\:*?"<>|\x00]', "", name).strip()
    if not safe_name:
        return jsonify({"error": "Name contains only invalid characters."}), 400

    ext = Path(file.filename).suffix.lower()
    if ext not in (".jpg", ".jpeg", ".png", ".bmp"):
        ext = ".jpg"

    timestamp = int(time.time())
    filename = f"{safe_name}_{timestamp}{ext}"

    Path(_dataset_path).mkdir(parents=True, exist_ok=True)
    dest = Path(_dataset_path) / filename

    tmp_dest = dest.with_suffix(dest.suffix + ".tmp")
    try:
        file.save(str(tmp_dest))
        tmp_dest.rename(dest)
    except Exception as exc:
        try:
            tmp_dest.unlink(missing_ok=True)
        except Exception:
            pass
        return jsonify({"error": f"nie udało się zapisać obrazu: {exc}"}), 500

    pkl = _get_pkl_path(_dataset_path)
    if pkl.exists():
        try:
            pkl.unlink()
        except Exception as e:
            print(f"Warning: nie udalo sie usunac cahse emmbedingow {pkl}: {e}")

    _embeddings_ready = False

    return jsonify({"nazwa pliku": filename}), 201

def _get_lan_ips() -> list[str]:
    ips: list[str] = []
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            s.connect(("8.8.8.8", 80))
            primary = s.getsockname()[0]
            if primary and not primary.startswith("127."):
                ips.append(primary)
    except Exception:
        pass
    try:
        hostname = socket.gethostname()
        for info in socket.getaddrinfo(hostname, None, socket.AF_INET):
            addr = info[4][0]
            if addr and not addr.startswith("127.") and addr not in ips:
                ips.append(addr)
    except Exception:
        pass
    return ips

def _build_db_only(dataset_path: str) -> None:
    dataset = Path(dataset_path)
    if not dataset.exists():
        print(f" Folder datasetu nie istnieje: {dataset_path}")
        sys.exit(1)

    imgs = sorted(
        f for f in dataset.rglob("*")
        if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"}
    )
    if not imgs:
        print(f"⚠  Brak zdjęć w folderze: {dataset_path}")
        sys.exit(0)

    pkl = _get_pkl_path(dataset_path)
    if pkl.exists():
        print(f" Baza embeddingów już istnieje: {pkl}")
        print("   Usuń plik .pkl jeśli chcesz przebudować bazę od zera.")
        sys.exit(0)

    est = _estimate_build_minutes(len(imgs))
    print(f"⏳ Budowanie bazy embeddingów dla {len(imgs)} zdjęć (~{est} min)…")
    print(f"   Model:   {MODEL_NAME} | Detektor: {DETECTOR}")
    print(f"   Wynik:   {pkl}")
    print("   Zostaw uruchomiony i wróć po zakończeniu.\n")

    t0 = time.time()
    from deepface import DeepFace
    print("   Wczytywanie modelu…")
    DeepFace.build_model(MODEL_NAME)
    print("   Model wczytany. Przetwarzanie zdjęć (postęp pokazywany poniżej):\n")

    try:
        DeepFace.find(
            img_path=str(imgs[0]),
            db_path=str(dataset),
            model_name=MODEL_NAME,
            distance_metric=DISTANCE_METRIC,
            detector_backend=DETECTOR,
            enforce_detection=False,
            align=True,
            silent=False,
        )
    except Exception as exc:
        print(f"\n Błąd podczas budowania bazy: {exc}")
        sys.exit(1)

    elapsed = time.time() - t0
    print(f"\n Baza embeddingów zbudowana w {elapsed:.0f} s ({elapsed / 60:.1f} min).")
    print(f"   Plik: {pkl}")
    print(f"\n   Teraz uruchom serwis normalnie:")
    print(f"      python service.py --dataset {dataset_path}")
    print("   Serwis będzie gotowy do pracy w ~30 s.\n")

def main():
    global _dataset_path, _warmup_start

    parser = argparse.ArgumentParser(
        description="Serwis rozpoznawania twarzy (gotowy model Facenet, bez trenowania)"
    )
    parser.add_argument("--dataset", default=str(DEFAULT_DATASET),
                        help=f"Folder z referencyjnymi zdjęciami twarzy (domyslnie: {DEFAULT_DATASET})")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT,
                        help=f"Port HTTP (domyslnie: {DEFAULT_PORT})")
    parser.add_argument("--host", default="0.0.0.0",
                        help="Adres nasluchiwania (domyslnie: 0.0.0.0)")
    parser.add_argument(
        "--build-db", action="store_true",
        help=(
            "Buduje bazę embeddingów (.pkl) dla datasetu i kończy działanie. "
            "Zalecane dla dużych datasetów (>200 zdjęć) – uruchom raz przed normalnym startem serwisu."
        ),
    )
    args = parser.parse_args()

    if args.build_db:
        _build_db_only(args.dataset)
        return

    _dataset_path = args.dataset

    if not Path(_dataset_path).exists():
        print(f" Folder datasetu nie istnieje: {_dataset_path}")
        print("  Utwórz folder i umieść w nim zdjęcia twarzy (np. Jan Kowalski_1.jpg)")
        print("  Serwis wystartuje, ale /recognize zwróci błąd 503 do czasu utworzenia folderu.")
    else:
        img_count = sum(1 for f in Path(_dataset_path).rglob("*")
                        if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"})
        print(f" Dataset: {_dataset_path} ({img_count} zdjęć)")
        if img_count > 200:
            pkl_exists, est = _check_pkl(_dataset_path, img_count)
            if not pkl_exists:
                print(f"\n     Duży dataset bez pliku .pkl – budowanie bazy zajmie ~{est} min.")
                print(f"      python service.py --build-db --dataset zeby przyspieszyc proves  {_dataset_path}\n")

    lan_ips = _get_lan_ips()

    print(f"\n Serwis startuje na http://{args.host}:{args.port}")
    print(f"   POST http://localhost:{args.port}/recognize  <- wyslij zdjecie twarzy (backend)")
    print(f"   GET  http://localhost:{args.port}/health     <- diagnostyka / status warmup")

    if args.host in ("0.0.0.0", "::") and lan_ips:
        print(f"\n   Adresy sieciowe (dostępne z innych urządzeń w sieci):")
        for ip in lan_ips:
            print(f"      http://{ip}:{args.port}/recognize")

    print(f"\n Aplikacja desktop/mobilna (MAUI) łączy się z backendem ASP.NET, nie z tym serwisem!")
    print(f"   URL backendu (zakodowany w aplikacji):")
    print(f"      Windows desktop:      http://localhost:{BACKEND_PORT}")
    print(f"      Emulator Android:     http://10.0.2.2:{BACKEND_PORT}")
    if lan_ips:
        for ip in lan_ips:
            print(f"      Fizyczne urządzenie:  http://{ip}:{BACKEND_PORT}")
    else:
        print(f"      Fizyczne urządzenie:  http://<IP-komputera>:{BACKEND_PORT}")

    print("\n Model wczytuje się w tle – serwis odpowiada na /health natychmiast,")
    print("   a na /recognize dopiero gdy model i baza embeddingów będą gotowe (patrz logi).\n")

    _warmup_start = time.time()
    warmup_thread = threading.Thread(target=_warmup, daemon=True, name="warmup")
    warmup_thread.start()

    app.run(host=args.host, port=args.port, debug=False, threaded=True)

if __name__ == "__main__":
    main()
