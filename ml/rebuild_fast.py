# rebuild_fast.py
import pickle
from pathlib import Path
from deepface import DeepFace
import time

dataset_path = Path("C:/Users/konra/Desktop/prace/twarze/rozpoznawanie-twarzy/ml/dataset")
print(f"Szukam zdjęć w: {dataset_path}")

# Szukaj we wszystkich podfolderach (w tym archive)
all_images = []
for ext in ['*.jpg', '*.jpeg', '*.png', '*.bmp']:
    all_images.extend(dataset_path.rglob(ext))

print(f"Znaleziono {len(all_images)} zdjęć")

if len(all_images) == 0:
    print("❌ NIE ZNALEZIONO ŻADNYCH ZDJĘĆ!")
    print("Sprawdź strukturę folderów:")
    for item in dataset_path.iterdir():
        print(f"  - {item.name} {'(folder)' if item.is_dir() else '(plik)'}")
    exit()

# Weź tylko 500 pierwszych zdjęć
images = all_images[:5000]
print(f"Testuję na pierwszych {len(images)} zdjęciach")

print("Ładowanie modelu Facenet...")
DeepFace.build_model("Facenet")
print("Model załadowany")

embeddings = []
filenames = []

start = time.time()
for i, img_path in enumerate(images):
    print(f"Przetwarzanie {i+1}/{len(images)}: {img_path.name} (z {img_path.parent.name})")
    try:
        emb = DeepFace.represent(
            img_path=str(img_path),
            model_name="Facenet",
            detector_backend="opencv",
            enforce_detection=False,
            align=True
        )
        if emb and len(emb) > 0:
            if isinstance(emb[0], dict) and 'embedding' in emb[0]:
                embeddings.append(emb[0]['embedding'])
            else:
                embeddings.append(emb[0])
            # Zachowaj względną ścieżkę żeby wiedzieć skąd jest zdjęcie
            rel_path = img_path.relative_to(dataset_path)
            filenames.append(str(rel_path))
        print(f"  ✓ Dodano")
    except Exception as e:
        print(f"  ✗ Błąd: {e}")

elapsed = time.time() - start
print(f"\nZbudowano {len(embeddings)} embeddingów w {elapsed:.0f}s")

# Zapisz w prostym formacie
output = dataset_path / "embeddings_test.pkl"
with open(output, 'wb') as f:
    pickle.dump((embeddings, filenames), f)

print(f"Zapisano do: {output}")
print(f"\nRozmiar pliku: {output.stat().st_size / 1024 / 1024:.1f} MB")