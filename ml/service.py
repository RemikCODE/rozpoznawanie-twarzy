import argparse
import os
import re
import socket
import sys
import tempfile
import threading
import time
import pickle
from pathlib import Path

from flask import Flask, jsonify, request
from flask_cors import CORS
from scipy.spatial.distance import cosine

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

_embeddings_cache = []
_filenames_cache = []

app = Flask(__name__)
CORS(app)

def _get_pkl_path(dataset_path: str) -> Path:
    # Używamy nowego pliku z embeddingami
    return Path(dataset_path) / "embeddings_test.pkl"

def _estimate_build_minutes(n_images: int) -> int:
    return max(1, round(n_images * _SECONDS_PER_IMAGE / 60))

def _check_pkl(dataset_path: str, img_count: int) -> tuple[bool, int]:
    exists = _get_pkl_path(dataset_path).exists()
    est = _estimate_build_minutes(img_count) if not exists and img_count > 0 else 0
    return exists, est

def _load_embeddings_to_memory():
    global _embeddings_cache, _filenames_cache
    
    pkl_path = _get_pkl_path(_dataset_path)
    if not pkl_path.exists():
        print(f" [Cache] Plik .pkl nie istnieje: {pkl_path}")
        return False
    
    try:
        print(f" [Cache] Ładowanie embeddingów z {pkl_path}...")
        t0 = time.time()
        
        with open(pkl_path, 'rb') as f:
            data = pickle.load(f)
        
        print(f" [Cache] Typ danych w pliku .pkl: {type(data)}")
        
        if isinstance(data, list):
            _embeddings_cache = data
            image_files = sorted([
                f for f in Path(_dataset_path).glob("*") 
                if f.suffix.lower() in ['.jpg', '.jpeg', '.png', '.bmp']
            ])
            _filenames_cache = [f.name for f in image_files[:len(_embeddings_cache)]]
            print(f" [Cache] Lista embeddingów: {len(_embeddings_cache)} elementów")
            print(f" [Cache] Dopasowano {len(_filenames_cache)} nazw plików")
            
        elif isinstance(data, tuple) and len(data) == 2:
            _embeddings_cache = data[0]
            paths = data[1]
            _filenames_cache = [Path(p).name for p in paths]
            print(f" [Cache] Tuple: {len(_embeddings_cache)} embeddingów, {len(_filenames_cache)} plików")
        else:
            print(f" [Cache] Nieobsługiwany format danych: {type(data)}")
            return False
        
        elapsed = time.time() - t0
        print(f" [Cache] Załadowano {len(_embeddings_cache)} embeddingów w {elapsed:.2f}s")
        if _filenames_cache:
            print(f" [Cache] Przykładowe pliki: {_filenames_cache[:3]}")
        return True
        
    except Exception as e:
        print(f" [Cache] Błąd ładowania embeddingów: {e}")
        import traceback
        traceback.print_exc()
        return False

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
                print("ℹ  [Embeddings] Dataset jest pusty.")
                _embeddings_ready = True
            else:
                pkl = _get_pkl_path(_dataset_path)
                pkl_exists, est = _check_pkl(_dataset_path, _embeddings_total)

                if pkl_exists:
                    print(f" [Embeddings] Znaleziono plik cache: {pkl.name}")
                    if _load_embeddings_to_memory():
                        print(f" [Embeddings] Gotowe do pracy z cache w RAM")
                        _embeddings_ready = True
                    else:
                        print(f" [Embeddings] Nie udało się załadować cache")
                        _embeddings_ready = True
                else:
                    print(f"[Embeddings] Brak pliku cache.")
                    _embeddings_ready = True
        else:
            print("  [Warmup] Brak datasetu")
            _embeddings_ready = True

        total = time.time() - _warmup_start
        print(f"\n[Warmup] Serwis gotowy! (czas: {total:.1f} s)")
        print(f"model: {MODEL_NAME}, detektor: {DETECTOR}")
        print(f"zdjęć w bazie: {_embeddings_total}")
        print(f"embeddingi w RAM: {len(_embeddings_cache)}")
        print("----------------------------------------\n")

    except Exception as exc:
        _warmup_error = str(exc)
        print(f"\n[Warmup] Błąd: {exc}")
        import traceback
        traceback.print_exc()

@app.route("/health", methods=["GET"])
def health():
    dataset_ok = _dataset_path is not None and Path(_dataset_path).exists()
    count = 0
    if dataset_ok:
        count = sum(1 for f in Path(_dataset_path).rglob("*")
                    if f.suffix.lower() in {".jpg", ".jpeg", ".png", ".bmp"})

    if not _model_ready:
        status = "error" if _warmup_error else "loading"
    else:
        status = "ok"

    return jsonify({
        "status": status,
        "model_ready": _model_ready,
        "embeddings_ready": _embeddings_ready,
        "embeddings_total": _embeddings_total,
        "embeddings_in_ram": len(_embeddings_cache),
        "warmup_error": _warmup_error,
        "warmup_elapsed_s": round(time.time() - _warmup_start, 1) if _warmup_start else 0,
        "dataset": str(_dataset_path),
        "dataset_exists": dataset_ok,
        "images_in_dataset": count,
        "model": MODEL_NAME,
    })

@app.route("/recognize", methods=["POST"])
def recognize():
    if not _model_ready:
        return jsonify({"error": "Model not ready"}), 503

    if "image" not in request.files:
        return jsonify({"error": "Brak obrazu"}), 400

    file = request.files["image"]
    tmp_path = None
    
    try:
        with tempfile.NamedTemporaryFile(suffix=".jpg", delete=False) as tmp:
            file.save(tmp.name)
            tmp_path = tmp.name

        if len(_embeddings_cache) > 0 and len(_filenames_cache) > 0:
            print("\n=== Używam cache w RAM ===")
            
            with _inference_lock:
                emb = _deepface.represent(
                    img_path=tmp_path,
                    model_name=MODEL_NAME,
                    detector_backend=DETECTOR,
                    enforce_detection=False,
                    align=True,
                )
            
            if not emb:
                return jsonify({"label": "", "confidence": 0.0})
            
            if isinstance(emb, list) and len(emb) > 0:
                if isinstance(emb[0], dict) and 'embedding' in emb[0]:
                    query = emb[0]['embedding']
                else:
                    query = emb[0]
            else:
                query = emb
            
            best_dist = float('inf')
            best_idx = -1
            
            for i, ref in enumerate(_embeddings_cache):
                try:
                    dist = cosine(query, ref)
                    if dist < best_dist:
                        best_dist = dist
                        best_idx = i
                except:
                    continue
            
            if best_dist < 0.4 and best_idx >= 0:
                label = _filenames_cache[best_idx]
                conf = max(0, min(100, (1 - best_dist) * 100))
                return jsonify({"label": label, "confidence": round(conf/100, 4)})
            
            return jsonify({"label": "", "confidence": 0.0})
        
        print("\n=== Używam DeepFace.find() ===")
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

        best = results[0].sort_values("distance").iloc[0]
        label = Path(str(best["identity"])).name
        conf = float(best.get("confidence", 0)) / 100.0

        return jsonify({"label": label, "confidence": conf})

    except Exception as e:
        return jsonify({"error": str(e)}), 500
    finally:
        if tmp_path:
            try:
                os.unlink(tmp_path)
            except:
                pass

@app.route("/add-person", methods=["POST"])
def add_person():
    global _embeddings_cache, _filenames_cache, _embeddings_total, _embeddings_ready

    if _dataset_path is None:
        return jsonify({"error": "Brak datasetu"}), 503

    name = request.form.get("name", "").strip()
    if not name:
        return jsonify({"error": "Brak nazwy"}), 400

    file = request.files.get("image")
    if not file:
        return jsonify({"error": "Brak pliku"}), 400

    # Bezpieczna nazwa pliku
    safe_name = re.sub(r'[/\\:*?"<>|\x00]', '', name).strip().replace(' ', '_')
    timestamp = int(time.time())
    filename = f"{safe_name}_{timestamp}.jpg"
    
    # Zapisz plik w dataset/archive/
    archive_path = Path(_dataset_path) / "archive"
    archive_path.mkdir(parents=True, exist_ok=True)
    dest = archive_path / filename
    
    try:
        file.save(dest)
        print(f" [Add] Zapisano plik w archive/: {filename}")
    except Exception as e:
        return jsonify({"error": f"Nie można zapisać: {e}"}), 500

    # === DODAJEMY DO ISTNIEJĄCEGO CACHE (embeddings_test.pkl) ===
    try:
        # Wyciągnij embedding dla nowego zdjęcia
        with _inference_lock:
            emb_result = _deepface.represent(
                img_path=str(dest),
                model_name=MODEL_NAME,
                detector_backend=DETECTOR,
                enforce_detection=False,
                align=True
            )
        
        if emb_result and len(emb_result) > 0:
            # Wyciągnij embedding
            if isinstance(emb_result[0], dict) and 'embedding' in emb_result[0]:
                new_embedding = emb_result[0]['embedding']
            else:
                new_embedding = emb_result[0]
            
            # DODAJ do istniejących cache (NIGDY NIE USUWAJ!)
            if _embeddings_cache is None:
                _embeddings_cache = []
            if _filenames_cache is None:
                _filenames_cache = []
                
            _embeddings_cache.append(new_embedding)
            _filenames_cache.append(filename)
            _embeddings_total += 1
            
            print(f" [Add] Dodano embedding do RAM. Teraz: {len(_embeddings_cache)} embeddingów")
            
            # ZAWSZE nadpisujemy embeddings_test.pkl (NIGDY nie usuwamy!)
            pkl_path = Path(_dataset_path) / "embeddings_test.pkl"
            with open(pkl_path, 'wb') as f:
                # Zapisz w formacie (embeddings, filenames)
                pickle.dump((_embeddings_cache, _filenames_cache), f)
            
            print(f" [Add] Zaktualizowano plik cache: embeddings_test.pkl")
            _embeddings_ready = True
        else:
            print(f" [Add] Nie udało się wyciągnąć embeddingu dla nowego zdjęcia")
            
    except Exception as e:
        print(f" [Add] Błąd podczas dodawania do cache: {e}")
        import traceback
        traceback.print_exc()
        # Nawet jeśli cache się nie zaktualizował, zdjęcie jest zapisane w archive/

    return jsonify({
        "filename": filename,
        "message": "Zdjęcie dodane do archive/ i embedding zaktualizowany w embeddings_test.pkl",
        "total_embeddings": len(_embeddings_cache)
    }), 201
def main():
    global _dataset_path, _warmup_start

    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", default=str(DEFAULT_DATASET))
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--host", default="0.0.0.0")
    args = parser.parse_args()

    _dataset_path = args.dataset
    _warmup_start = time.time()
    
    print(f"\n=== Face Recognition ML Service ===")
    print(f"Dataset: {_dataset_path}")
    print(f"Port: {args.port}")
    print("====================================\n")
    
    warmup_thread = threading.Thread(target=_warmup, daemon=True)
    warmup_thread.start()
    
    app.run(host=args.host, port=args.port, debug=False, threaded=True)

if __name__ == "__main__":
    main()