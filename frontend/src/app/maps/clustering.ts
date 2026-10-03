import { MapBounds, MapEvent } from '../public/public-event.models';

export interface MapCluster {
  latitude: number;
  longitude: number;
  events: MapEvent[];
  /** Box around the members; clicking a cluster zooms to it. */
  bounds: MapBounds;
}

const TILE_SIZE = 256;

/** Web Mercator world pixel coordinates at a zoom level, as Google Maps draws them. */
export function toWorldPixels(latitude: number, longitude: number, zoom: number): { x: number; y: number } {
  const scale = TILE_SIZE * 2 ** zoom;
  const clamped = Math.max(-85.05112878, Math.min(85.05112878, latitude));
  const sin = Math.sin((clamped * Math.PI) / 180);
  return {
    x: ((longitude + 180) / 360) * scale,
    y: (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale,
  };
}

/**
 * Grid-based clustering as in Google's MarkerClusterer: project every event to screen pixels at the
 * current zoom; each cluster owns a square of ±`gridPx` pixels around its first event, and an event
 * joins the first cluster whose square contains it, otherwise it starts a new one. Unlike fixed
 * grid cells, two events a few pixels apart are never split just because a cell edge runs between
 * them. Zooming in doubles pixel distances, so clusters break up naturally, while events at the very
 * same spot stay together and open as a list. Events are visited in a fixed order (north to south,
 * then west to east, then id), so the result is deterministic. O(n · clusters) per render.
 */
export function clusterEvents(events: readonly MapEvent[], zoom: number, gridPx = 60): MapCluster[] {
  const ordered = [...events].sort(
    (a, b) => b.latitude - a.latitude || a.longitude - b.longitude || a.id.localeCompare(b.id),
  );
  const clusters: { anchor: { x: number; y: number }; events: MapEvent[] }[] = [];

  for (const event of ordered) {
    const point = toWorldPixels(event.latitude, event.longitude, zoom);
    const home = clusters.find(
      (cluster) => Math.abs(cluster.anchor.x - point.x) <= gridPx && Math.abs(cluster.anchor.y - point.y) <= gridPx,
    );
    if (home) home.events.push(event);
    else clusters.push({ anchor: point, events: [event] });
  }

  return clusters.map(({ events: members }) => {
    const latitudes = members.map((event) => event.latitude);
    const longitudes = members.map((event) => event.longitude);
    return {
      latitude: latitudes.reduce((sum, value) => sum + value, 0) / members.length,
      longitude: longitudes.reduce((sum, value) => sum + value, 0) / members.length,
      events: members,
      bounds: {
        north: Math.max(...latitudes),
        south: Math.min(...latitudes),
        east: Math.max(...longitudes),
        west: Math.min(...longitudes),
      },
    };
  });
}
