# Simple server for iBridge and SMS3. Doesn't really work all that good but its just an example to see 
import os
import re
import threading
import time
from dataclasses import dataclass, asdict
from typing import Dict, List, Optional
from fastapi import FastAPI, Query, HTTPException
from mutagen.flac import FLAC
import vlc

ROOT = r"Z:\Media\music"
app = FastAPI(title="SMS3/ibRidge server")

def Normalize(s: Optional[str]) -> str:
    if not s:
        return "Unknown"
    s = s.strip()
    return s if s else "Unknown"

def FirstTag(tags, *keys) -> Optional[str]:
    for k in keys:
        if k in tags and tags[k]:
            return str(tags[k][0])
    return None

def ParsetrackNum(track_str: Optional[str]) -> int:
    if not track_str:
        return 0
    m = re.match(r"^\s*(\d+)", track_str)  # "3", "03", "3/12"
    return int(m.group(1)) if m else 0

def WalkForFlacs(root: str) -> List[str]:
    out = []
    for dirpath, _, filenames in os.walk(root):
        for fn in filenames:
            if fn.lower().endswith(".flac"):
                out.append(os.path.join(dirpath, fn))
    return out

def TopLevel(root: str, file_path: str) -> str:
    rel = os.path.relpath(file_path, root)
    parts = rel.split(os.sep)
    return parts[0] if len(parts) > 1 else "Library"

@dataclass
class Track:
    title: str
    artist: str
    album: str
    genre: str
    track: int
    path: str
    playlist: str

@dataclass
class Catalog:
    playlists: List[str]
    genres: List[str]
    artists: List[str]
    albums: Dict[str, List[str]]
    songs: Dict[str, List[str]]
    tracks: List[Track]

CATALOG: Optional[Catalog] = None

def Build() -> Catalog:
    flacs = WalkForFlacs(ROOT)

    tracks: List[Track] = []
    playlists_set = set()
    genres_set = set()
    artists_set = set()
    albums_by_artist: Dict[str, set] = {}
    tracks_by_album: Dict[str, List[Track]] = {}

    for fp in flacs:
        try:
            audio = FLAC(fp)
            tags = audio.tags or {}

            title = Normalize(FirstTag(tags, "title"))
            artist = Normalize(FirstTag(tags, "artist", "albumartist"))
            album = Normalize(FirstTag(tags, "album"))
            genre = Normalize(FirstTag(tags, "genre"))
            trackno = ParsetrackNum(FirstTag(tags, "tracknumber", "track"))

            playlist = TopLevel(ROOT, fp)

            playlists_set.add(playlist)
            genres_set.add(genre)
            artists_set.add(artist)
            albums_by_artist.setdefault(artist, set()).add(album)

            t = Track(
                title=title,
                artist=artist,
                album=album,
                genre=genre,
                track=trackno,
                path=fp,
                playlist=playlist,
            )
            tracks.append(t)
            tracks_by_album.setdefault(album, []).append(t)
        except Exception:
            continue

    albums: Dict[str, List[str]] = {
        a: sorted(list(albums_by_artist[a]), key=lambda x: x.lower())
        for a in albums_by_artist
    }

    artists = sorted(list(artists_set), key=lambda x: x.lower())
    genres = sorted(list(genres_set), key=lambda x: x.lower())
    playlists = sorted(list(playlists_set), key=lambda x: x.lower())

    songs: Dict[str, List[str]] = {}
    for album, tlist in tracks_by_album.items():
        tlist_sorted = sorted(tlist, key=lambda t: (t.track, t.title.lower()))
        songs[album] = [t.title for t in tlist_sorted]

    sortedtracks = sorted(tracks, key=lambda t: (t.artist.lower(), t.album.lower(), t.track, t.title.lower()))

    return Catalog(
        playlists=playlists,
        genres=genres,
        artists=artists,
        albums=albums,
        songs=songs,
        tracks=sortedtracks
    )

def Ensure() -> Catalog:
    global CATALOG
    if CATALOG is None:
        CATALOG = Build()
    return CATALOG

class VLC:
    def __init__(self):
        self.instance = vlc.Instance()
        self.list_player = self.instance.media_list_player_new()
        self.player = self.list_player.get_media_player()

        self.lock = threading.Lock()
        self.queue: List[Track] = []
        self.queue_index: int = 0
        self.started_at: float = 0.0

        em = self.player.event_manager()
        em.event_attach(vlc.EventType.MediaPlayerEndReached, self.OnEnd)

    def OnEnd(self, event):
        with self.lock:
            if self.queue:
                self.queue_index = min(self.queue_index + 1, len(self.queue) - 1)

    def Queue(self, tracks: List[Track], start_index: int):
        with self.lock:
            if not tracks:
                raise ValueError("empty queue")

            self.queue = tracks
            self.queue_index = max(0, min(start_index, len(tracks) - 1))

            ml = self.instance.media_list_new()
            for t in tracks:
                m = self.instance.media_new_path(t.path)
                ml.add_media(m)

            self.list_player.set_media_list(ml)
            self.list_player.play_item_at_index(self.queue_index)
            self.started_at = time.time()

    def play_album(self, album: str) -> Track:
        cat = Ensure()
        tracks = [t for t in cat.tracks if t.album == album]
        tracks = sorted(tracks, key=lambda t: (t.track, t.title.lower()))
        if not tracks:
            raise KeyError(f"album not found: {album}")
        self.Queue(tracks, 0)
        return tracks[0]

    def play_artist(self, artist: str) -> Track:
        cat = Ensure()
        tracks = [t for t in cat.tracks if t.artist == artist]
        tracks = sorted(tracks, key=lambda t: (t.album.lower(), t.track, t.title.lower()))
        if not tracks:
            raise KeyError(f"artist not found: {artist}")
        self.Queue(tracks, 0)
        return tracks[0]

    def play_track(self, album: str, title: str) -> Track:
        cat = Ensure()
        album_tracks = [t for t in cat.tracks if t.album == album]
        album_tracks = sorted(album_tracks, key=lambda t: (t.track, t.title.lower()))
        if not album_tracks:
            raise KeyError(f"album not found: {album}")

        idx = None
        for i, t in enumerate(album_tracks):
            if t.title == title:
                idx = i
                break
        if idx is None:
            for i, t in enumerate(album_tracks):
                if t.title.lower() == title.lower():
                    idx = i
                    break
        if idx is None:
            raise KeyError(f"track not found: {title} in album {album}")

        self.Queue(album_tracks, idx)
        return album_tracks[idx]

    def next(self):
        with self.lock:
            self.list_player.next()
            if self.queue:
                self.queue_index = min(self.queue_index + 1, len(self.queue) - 1)

    def prev(self):
        with self.lock:
            self.list_player.previous()
            if self.queue:
                self.queue_index = max(self.queue_index - 1, 0)

    def pause(self):
        with self.lock:
            self.player.pause()

    def resume(self):
        with self.lock:
            self.player.play()

    def stop(self):
        with self.lock:
            self.list_player.stop()

    def set_volume(self, vol: int):
        vol = max(0, min(vol, 100))
        with self.lock:
            self.player.audio_set_volume(vol)

    def status(self) -> dict:
        with self.lock:
            state = self.player.get_state()
            vol = self.player.audio_get_volume()
            pos = self.player.get_time()  # ms
            dur = self.player.get_length()  # ms

            now = None
            if self.queue:
                now = self.queue[self.queue_index]

            return {
                "state": str(state),
                "volume": vol,
                "position_ms": pos,
                "duration_ms": dur,
                "index": self.queue_index,
                "queue_len": len(self.queue),
                "now": asdict(now) if now else None,
            }

PLAYER = VLC()

@app.get("/health")
def health():
    return {"ok": True, "root": ROOT}

@app.post("/rescan")
def rescan():
    global CATALOG
    CATALOG = Build()
    return {"ok": True, "tracks": len(CATALOG.tracks)}

@app.get("/catalog")
def catalog():
    cat = Ensure()
    d = asdict(cat)
    d["tracks"] = [asdict(t) for t in cat.tracks]
    return d

@app.get("/playlists")
def playlists():
    return Ensure().playlists

@app.get("/genres")
def genres():
    return Ensure().genres

@app.get("/artists")
def artists():
    return Ensure().artists

@app.get("/albums")
def albums():
    return Ensure().albums

@app.get("/songs")
def songs():
    return Ensure().songs

@app.get("/play/artist")
def play_artist(name: str = Query(...), volume: int = Query(80)):
    cat = Ensure()
    artist = Normalize(name)
    if artist not in cat.artists:
        raise HTTPException(status_code=404, detail=f"artist not found: {artist}")
    try:
        PLAYER.set_volume(volume)
        chosen = PLAYER.play_artist(artist)
        return {"ok": True, "type": "artist", "requested": artist, "track": asdict(chosen)}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/play/album")
def play_album(name: str = Query(...), volume: int = Query(80)):
    cat = Ensure()
    album = Normalize(name)
    if album not in cat.songs:
        raise HTTPException(status_code=404, detail=f"album not found: {album}")
    try:
        PLAYER.set_volume(volume)
        chosen = PLAYER.play_album(album)
        return {"ok": True, "type": "album", "requested": album, "track": asdict(chosen)}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/play/track")
def play_track(album: str = Query(...), title: str = Query(...), volume: int = Query(80)):
    Ensure()
    album_n = Normalize(album)
    title_n = Normalize(title)
    try:
        PLAYER.set_volume(volume)
        chosen = PLAYER.play_track(album_n, title_n)
        return {"ok": True, "type": "track", "requested": {"album": album_n, "title": title_n}, "track": asdict(chosen)}
    except KeyError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/next")
def next_track():
    PLAYER.next()
    return {"ok": True, **PLAYER.status()}

@app.get("/prev")
def prev_track():
    PLAYER.prev()
    return {"ok": True, **PLAYER.status()}

@app.get("/pause")
def pause():
    PLAYER.pause()
    return {"ok": True, **PLAYER.status()}

@app.get("/resume")
def resume():
    PLAYER.resume()
    return {"ok": True, **PLAYER.status()}

@app.get("/stop")
def stop():
    PLAYER.stop()
    return {"ok": True, **PLAYER.status()}

@app.get("/volume")
def volume_set(v: int = Query(...)):
    PLAYER.set_volume(v)
    return {"ok": True, **PLAYER.status()}

@app.get("/status")
def status():
    return {"ok": True, **PLAYER.status()}
    
if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=3100)
