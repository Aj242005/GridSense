import os, zipfile, trimesh

downloads = 'C:/Users/AKSHIT JAIN/Downloads'

track_zips = {
    'Bahrain': 'bahrain_international_circuit.zip',
    'Shanghai': 'shanghai_international_circuit_2018_layout.zip',
    'Suzuka': 'suzuka_circuit_2001_layout.zip',
    'YasMarina': 'yas_marina_circuit_abu_dhabi_2021_layout.zip',
    'RedBullRing': 'redbull_ring_2025_layout.zip',
}

base_dest = 'Assets/ImportedTracks'
os.makedirs(base_dest, exist_ok=True)

print("=== EXTRACTING AND CONVERTING DOWNLOADED TRACKS ===")

for track_name, zip_name in track_zips.items():
    zip_path = os.path.join(downloads, zip_name)
    dest_dir = os.path.join(base_dest, track_name)
    os.makedirs(dest_dir, exist_ok=True)
    
    if not os.path.exists(zip_path):
        print(f"Skipping {track_name}: {zip_path} not found")
        continue

    print(f"\n[EXTRACTING] {track_name} ({zip_name})...")
    with zipfile.ZipFile(zip_path, 'r') as z:
        z.extractall(dest_dir)
    
    gltf_file = os.path.join(dest_dir, 'scene.gltf')
    if os.path.exists(gltf_file):
        print(f"  Converting {gltf_file} to OBJ...")
        try:
            scene = trimesh.load(gltf_file)
            obj_path = os.path.join(dest_dir, f"{track_name.lower()}_sketchfab.obj")
            scene.export(obj_path)
            sz_mb = os.path.getsize(obj_path) / (1024 * 1024)
            print(f"  [SUCCESS] Exported {obj_path} ({sz_mb:.1f} MB)")
        except Exception as e:
            print(f"  Error converting {track_name}: {e}")

print("\n=== ALL TRACKS PROCESSED ===")
