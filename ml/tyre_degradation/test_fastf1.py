import fastf1
import os

cache_dir = os.path.join(os.path.dirname(__file__), 'cache')
os.makedirs(cache_dir, exist_ok=True)
fastf1.Cache.enable_cache(cache_dir)

print(f"FastF1 cache enabled at: {cache_dir}")
session = fastf1.get_session(2023, 'Bahrain', 'R')
print(f"Loaded session metadata: {session.event['EventName']}, Year: {session.event['EventDate']}")
