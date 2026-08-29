import urllib.request, json, os, zipfile, io

token = '1bc0a88dbd784a7f87c2fb6f435c8d77'
headers = {
    'Authorization': f'Token {token}',
    'User-Agent': 'GridSense-Agent/1.0'
}

def download_model(uid, dest_dir):
    print(f"\n[DOWNLOAD] Requesting download for UID: {uid}...")
    url = f"https://api.sketchfab.com/v3/models/{uid}/download"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        print(f"Error fetching download link: {e}")
        return False

    download_url = None
    if 'gltf' in data and 'url' in data['gltf']:
        download_url = data['gltf']['url']
        print(f"  Got glTF download link, size: {data['gltf'].get('size', 0)/(1024*1024):.1f} MB")
    elif 'source' in data and 'url' in data['source']:
        download_url = data['source']['url']
        print(f"  Got Source download link, size: {data['source'].get('size', 0)/(1024*1024):.1f} MB")
    else:
        print("  No direct download URL found in response:", data)
        return False

    print("  Downloading archive...")
    req_dl = urllib.request.Request(download_url, headers={'User-Agent': 'GridSense-Agent/1.0'})
    with urllib.request.urlopen(req_dl) as resp:
        zip_bytes = resp.read()
    
    print(f"  Downloaded {len(zip_bytes)/(1024*1024):.1f} MB. Extracting to {dest_dir}...")
    os.makedirs(dest_dir, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as z:
        z.extractall(dest_dir)
    
    print("  Extraction complete! Extracted files:")
    for f in os.listdir(dest_dir):
        print(f"    - {f}")
    return True

# Download McLaren MCL60 and Red Bull RB20
models_to_fetch = [
    ('8340e27c325345e4aa92a6a31cc34b1e', 'Assets/ImportedModels/McLaren_MCL60'),
    ('4315ff70ec694ef0b7ebfb84145ef48c', 'Assets/ImportedModels/RedBull_RB20')
]

for uid, target_path in models_to_fetch:
    download_model(uid, target_path)

print("\n=== SKETCHFAB DOWNLOAD PROCESS FINISHED ===")
