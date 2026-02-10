import requests
import json
import time
from bs4 import BeautifulSoup
import re

stationstxt = 'XM_CHANNELS.json'
stationssrc = 'stations.txt'

def loadstationstxt():
    try:
        with open(stationstxt, 'r') as file:
            stations = json.load(file)
        return stations
    except FileNotFoundError:
        return {}

def savestations(stations):
    with open(stationstxt, 'w') as file:
        json.dump(stations, file, indent=4)

def loadstations():
    stations = []
    try:
        with open(stationssrc, 'r', encoding='utf-8') as file:
            lines = file.readlines()
    except UnicodeDecodeError:
        with open(stationssrc, 'r', encoding='ISO-8859-1') as file:
            lines = file.readlines()
    
    for line in lines:
        parts = line.strip().split('|')
        if len(parts) >= 3:
            station_id = parts[0].strip()
            channel_number = parts[1].strip()
            station_name = parts[2].strip()
            station_name = f"{channel_number}: {station_name}"
            stations.append((station_id, station_name))
    return stations
    
def getxmurl(channel_name):
    channel_name_clean = channel_name.split(": ", 1)[-1]
    channel_name_clean = channel_name_clean.replace("'", "")

    slug = channel_name_clean.replace(" ", "-").lower()
    search_url = f"https://www.siriusxm.com/channels/{slug}"
    print(f"channels URL: {search_url}")

    response = requests.get(search_url)
    if response.status_code != 200:
        print(f"unable to retrieve page for {channel_name_clean}")
        return None

    soup = BeautifulSoup(response.text, "html.parser")

    link = soup.find("a", {"aria-label": f"listen live to {channel_name_clean}"})
    if link and link.get("href"):
        return link["href"]

    link = soup.find("a", {"is": "sxm-player-link"})
    if link and link.get("href"):
        return link["href"]

    meta = soup.find("meta", {"name": "deeplink"})
    if meta and meta.get("content"):
        return meta["content"]

    for a in soup.find_all("a", href=True):
        if "sxm.app.link" in a["href"]:
            return a["href"]

    print(f"-- no redirect link found for {channel_name_clean} --")
    return None

def getxmid(app_link, station_name):
    if not app_link:
        print(f"skipping {station_name}, no link found")
        return None

    headers = {
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) "
            "Chrome/120.0.0.0 Safari/537.36"
        ),
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "en-US,en;q=0.9",
    }

    session = requests.Session()
    response = session.get(app_link, headers=headers, allow_redirects=True)
    final_url = response.url

    m = re.search(
        r"entity/([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})",
        final_url,
        re.IGNORECASE,
    )
    if m:
        uuid = m.group(1)
        print(f"ID (entity) found for {station_name}: {uuid}")
        return uuid

    m2 = re.search(
        r"channel-linear/[A-Za-z0-9\-]+/([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})",
        final_url,
        re.IGNORECASE,
    )
    if m2:
        uuid = m2.group(1)
        print(f"ID (linear) found for {station_name}: {uuid}")
        return uuid

    m3 = re.search(
        r"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}",
        final_url,
        re.IGNORECASE,
    )
    if m3:
        uuid = m3.group(0)
        print(f"ID found for {station_name}: {uuid}")
        return uuid

    print(f"-- ID not found for {station_name} --")
    return None

def updatejson(station_id, station_name, channel_id):
    stations = loadstationstxt()

    if station_name in stations:
        stations[station_name]["xmchannel"] = channel_id if channel_id else "None"
        if channel_id:
            print(f"updated {station_name} with ID: {channel_id}")
        else:
            print(f"updated {station_name} with ID: None (ID not found)")
    else:
        stations[station_name] = {
            "xmurl": f"http://127.0.0.1:9999/{station_id}.m3u8",
            "xmchannel": channel_id if channel_id else "None"
        }
        if channel_id:
            print(f"added {station_name} with ID {channel_id}")
        else:
            print(f"added {station_name} with ID: None (ID not found)")

    savestations(stations)

def main():
    stations = loadstations()

    for station_id, station_name in stations:
        print(f"-- processing {station_name}... --")

        app_link = getxmurl(station_name)
        if app_link:
            channel_id = getxmid(app_link, station_name)
        else:
            channel_id = None

        updatejson(station_id, station_name, channel_id)
        
        time.sleep(1)

if __name__ == "__main__":
    main()
