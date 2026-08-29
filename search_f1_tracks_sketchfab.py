import urllib.request, json, urllib.parse

token = '1bc0a88dbd784a7f87c2fb6f435c8d77'
headers = {
    'Authorization': f'Token {token}',
    'User-Agent': 'GridSense-Agent/1.0'
}

def search_tracks(query):
    url = f"https://api.sketchfab.com/v3/search?type=models&downloadable=true&q={urllib.parse.quote(query)}"
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('results', [])
    except Exception as e:
        print(f"Error searching {query}: {e}")
        return []

track_queries = [
    'bahrain circuit', 'shanghai circuit', 'suzuka circuit', 'yas marina circuit',
    'f1 circuit track', 'race track circuit', 'silverstone circuit', 'spa francorchamps', 'monza circuit', 'red bull ring'
]

print("=== SEARCHING SKETCHFAB FOR DOWNLOADABLE F1 CIRCUITS / TRACKS ===")
seen = set()
for tq in track_queries:
    results = search_tracks(tq)
    print(f"\n--- Results for '{tq}' ---")
    for r in results[:4]:
        uid = r.get('uid')
        if uid in seen: continue
        seen.add(uid)
        name = r.get('name')
        faces = r.get('faceCount')
        url = r.get('viewerUrl')
        print(f"  [{uid}] {name} ({faces:,} polys) -> {url}")
