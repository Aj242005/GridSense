import urllib.request, json, os

token = '1bc0a88dbd784a7f87c2fb6f435c8d77'
headers = {
    'Authorization': f'Token {token}',
    'User-Agent': 'GridSense-Agent/1.0'
}

def get(url):
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        return {'error': str(e)}

print('=== SKETCHFAB USER PROFILE ===')
me = get('https://api.sketchfab.com/v3/me')
print('Username:', me.get('username'))
print('Display Name:', me.get('displayName'))
print('Profile URL:', me.get('profileUrl'))

print('\n=== SKETCHFAB LIKED MODELS ===')
likes = get('https://api.sketchfab.com/v3/me/likes')
for item in likes.get('results', []):
    print(f"  - UID: {item.get('uid')} | Name: {item.get('name')} | Downloadable: {item.get('isDownloadable')} | URL: {item.get('viewerUrl')}")

print('\n=== SKETCHFAB COLLECTIONS ===')
cols = get('https://api.sketchfab.com/v3/me/collections')
for item in cols.get('results', []):
    print(f"  - UID: {item.get('uid')} | Name: {item.get('name')} | Count: {item.get('modelsCount')}")
    # fetch models in collection
    col_models = get(f"https://api.sketchfab.com/v3/collections/{item.get('uid')}/models")
    for cm in col_models.get('results', []):
        print(f"      * UID: {cm.get('uid')} | Name: {cm.get('name')} | Downloadable: {cm.get('isDownloadable')} | URL: {cm.get('viewerUrl')}")

print('\n=== SKETCHFAB USER MODELS ===')
models = get('https://api.sketchfab.com/v3/me/models')
for item in models.get('results', []):
    print(f"  - UID: {item.get('uid')} | Name: {item.get('name')} | Downloadable: {item.get('isDownloadable')}")

# Also search Sketchfab for high-quality F1 2024 / 2026 cars & McLaren / Red Bull
print('\n=== SEARCHING SKETCHFAB FOR DOWNLOADABLE F1 ASSETS ===')
search_f1 = get('https://api.sketchfab.com/v3/search?type=models&downloadable=true&q=formula+1+car')
for item in search_f1.get('results', [])[:8]:
    print(f"  - UID: {item.get('uid')} | Name: {item.get('name')} | FaceCount: {item.get('faceCount')} | License: {item.get('license', {}).get('label')}")
