const express = require("express");
const bodyParser = require("body-parser");
const RoonApi = require("node-roon-api");
const RoonApiBrowse = require("node-roon-api-browse");
const RoonApiTransport = require("node-roon-api-transport");
const app = express();
app.use(bodyParser.json());

let ConnectedCore = null;
let Browse = null;
let Transport = null;
let zones = {};
let stations = [];
let CurrentStation = 0;
const BluesoundZone = "IntrnetRadio NODE 2i";

const roon = new RoonApi({
  extension_id: "com.rxm.radio",
  display_name: "RXM",
  display_version: "1.0.0",
  publisher: "Roon",
  email: "harry@zawg.ca",
  core_paired(core) {
    console.log("paired to core:", core.display_name);
    ConnectedCore = core;
    Browse = core.services.RoonApiBrowse;
    Transport = core.services.RoonApiTransport;
    Transport.subscribe_zones((cmd, data) => {
      if (cmd === "Subscribed") {
        zones = {};
        data.zones.forEach(z => zones[z.zone_id] = z);
      }
      if (cmd === "ZoneAdded" || cmd === "ZoneChanged") {
        zones[data.zone.zone_id] = data.zone;
      }
      if (cmd === "Changed" && data.zones_changed) {
        data.zones_changed.forEach(z => zones[z.zone_id] = z);
      }
      if (cmd === "ZoneRemoved") delete zones[data.zone_id];
    });
  },
  core_unpaired(core) {
    console.log("unpaired from core", core.display_name);
    ConnectedCore = null;
    Browse = null;
    Transport = null;
  }
});

roon.init_services({ required_services: [RoonApiBrowse, RoonApiTransport] });
roon.start_discovery();

async function Load() {
  if (!Browse) return [];
  const { list } = await new Promise((resolve, reject) => {
    Browse.browse({ hierarchy: "internet_radio" }, (err, body) => {
      if (err) return reject(err);
      resolve(body);
    });
  });
  if (!list) return [];
  return new Promise((resolve, reject) => {
    Browse.load({ hierarchy: "internet_radio", level: 0, count: list.count }, (err, body) => {
      if (err) return reject(err);
      resolve(body.items || []);
    });
  });
}

function GetZone(name) {
  return Object.values(zones).find(z => z.display_name === name) || null;
}

async function Play(index) {
  await EnsureStations();
  const zone = GetZone(BluesoundZone);
  if (!zone) throw new Error("Enable Extension");

  const station = stations[index];
  Browse.browse({
    hierarchy: "internet_radio",
    item_key: station.item_key,
    action: "play",
    zone_or_output_id: zone.zone_id
  }, () => {});

  return { index, title: station.title, subtitle: station.subtitle };
}

async function EnsureStations() {
  if (!stations.length) {
    stations = await Load();
    CurrentStation = 0;
    const zone = GetZone(BluesoundZone);
    if (zone && stations.length) {
      const first = stations[0];
      console.log(`[RoonApi] Playing first station ${first.title}`);
      Browse.browse({
        hierarchy: "internet_radio",
        item_key: first.item_key,
        action: "play",
        zone_or_output_id: zone.zone_id
      }, () => {});
    }
  }
}

app.get("/radio/playing", (req, res) => {
  const zone = GetZone(BluesoundZone);
  if (!ConnectedCore) return res.status(500).json({ message: "Enable Extension" });

  const np = zone.now_playing || {};
  res.json({
    zone: zone.display_name,
    state: zone.state,
    playing: zone.state === "playing",
    artist: np.three_line?.line1 || np.two_line?.line1 || null,
    title: np.three_line?.line2 || np.two_line?.line2 || null,
    station: np.three_line?.line3 || np.one_line?.line1 || null
  });
});

app.get("/radio/next", async (req, res) => {
  try {
    if (!ConnectedCore) return res.status(500).json({ message: "Enable Extension" });
    CurrentStation = (CurrentStation + 1) % stations.length;

    const result = await Play(CurrentStation);
    res.json(result);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: err.message });
  }
});

app.get("/radio/prev", async (req, res) => {
  try {
    if (!ConnectedCore) return res.status(500).json({ message: "Enable Extension" });
    CurrentStation = (CurrentStation - 1 + stations.length) % stations.length;

    const result = await Play(CurrentStation);
    res.json(result);
  } catch (err) {
    console.error(err);
    res.status(500).json({ message: err.message });
  }
});

app.get("/radio/pause", (req, res) => {
    try {
        if (!ConnectedCore) return res.status(500).json({ message: "Enable Extension" });
        const zone = GetZone(BluesoundZone);

        Transport.control(zone.zone_id, "pause", (err) => {
            if (err) {
                console.error("[RoonApi] Error trying to pause:", err);
                return res.status(500).json({ error: err.message });
            }
            res.json({
                message: "Paused",
                zone: zone.display_name,
            });
        });
    } catch (err) {
        console.error(err);
        res.status(500).json({ message: err.message });
    }
});

app.get("/radio/play", (req, res) => {
    try {
        if (!ConnectedCore || !Transport) {
            return res.status(500).json({ message: "Enable Extension" });
        }
        const zone = GetZone(BluesoundZone);

        Transport.control(zone.zone_id, "play", (err) => {
            if (err) {
                console.error("[RoonApi] Error trying to play:", err);
                return res.status(500).json({ error: err.message });
            }
            res.json({
                message: "Playing",
                zone: zone.display_name,
            });
        });
    } catch (err) {
        console.error(err);
        res.status(500).json({ message: err.message });
    }
});

app.get("/radio/stations", async (req, res) => {
  try {
    await EnsureStations();
    res.json(stations.map(s => ({ title: s.title, subtitle: s.subtitle })));
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: err.message });
  }
});

const PORT = 3000;
app.listen(PORT, () => console.log(`Listening on port ${PORT}`));
