import urllib.request, json

token = '1bc0a88dbd784a7f87c2fb6f435c8d77'
headers = {
    'Authorization': f'Token {token}',
    'User-Agent': 'GridSense-Agent/1.0'
}

def search(query):
    url = f"https://api.sketchfab.com/v3/search?type=models&downloadable=true&q={urllib.parse.quote(query)}"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('results', [])
    except Exception as e:
        print(f"Error searching {query}: {e}")
        return []

queries = ['mclaren f1 2024', 'red bull f1 2024', 'ferrari f1 2024', 'formula 1 2024', 'f1 2023 car']
seen = set()

print("=== BEST DOWNLOADABLE F1 3D MODELS ON SKETCHFAB ===")
for q in queries:
    results = search(q)
    for r in results[:5]:
        uid = r.get('uid')
        if uid in seen: continue
        seen.add(uid)
        name = r.get('name')
        faces = r.get('faceCount')
        url = r.get('viewerUrl')
        thumb = r.get('thumbnails', {}).get('images', [{}])[0].get('url', '')
        print(f"[{uid}] {name} ({faces:,} polys) -> {url}")
