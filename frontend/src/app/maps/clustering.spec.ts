import { MapEvent } from '../public/public-event.models';
import { clusterEvents, toWorldPixels } from './clustering';

function event(id: string, latitude: number, longitude: number): MapEvent {
  return { id, title: id, startAt: '2027-01-01T10:00:00Z', venueName: 'V', locality: 'L', address: 'A', latitude, longitude, category: 'Music' };
}

describe('clusterEvents', () => {
  const kiryatShmona = event('ks', 33.2073, 35.57);
  const telHai = event('th', 33.234, 35.579);
  const tiberias = event('tb', 32.7922, 35.5312);
  const sameVenue = event('ks-2', 33.2073, 35.57);
  const all = [kiryatShmona, telHai, tiberias, sameVenue];

  it('projects to Web Mercator pixels like Google Maps', () => {
    expect(toWorldPixels(0, 0, 0)).toEqual({ x: 128, y: 128 });
    const northEast = toWorldPixels(85.05112878, 180, 1);
    expect(northEast.x).toBeCloseTo(512, 6);
    expect(northEast.y).toBeCloseTo(0, 3);
  });

  it('puts every event in exactly one cluster at any zoom', () => {
    for (const zoom of [5, 9, 12, 15, 19]) {
      const clusters = clusterEvents(all, zoom);
      const ids = clusters.flatMap((cluster) => cluster.events.map((item) => item.id)).sort();
      expect(ids).toEqual(all.map((item) => item.id).sort());
    }
  });

  it('merges nearby towns when zoomed out and splits them when zoomed in', () => {
    expect(clusterEvents(all, 7).length).toBe(1);
    // About 1 km per pixel at zoom 7 (46 km to Tiberias is ~45 px); ~128 m per pixel at zoom 10.
    const regional = clusterEvents(all, 10);
    expect(regional.length).toBe(2);
    expect(clusterEvents(all, 15).length).toBe(3);
  });

  it('keeps events at the same spot together at the closest zoom', () => {
    const closest = clusterEvents(all, 21);
    const venue = closest.find((cluster) => cluster.events.some((item) => item.id === 'ks'))!;
    expect(venue.events.map((item) => item.id).sort()).toEqual(['ks', 'ks-2']);
    expect(venue.bounds.north).toBe(venue.bounds.south);
  });

  it('places the marker at the centroid inside the bounds', () => {
    const [cluster] = clusterEvents([kiryatShmona, telHai], 8);
    expect(cluster.latitude).toBeCloseTo((33.2073 + 33.234) / 2, 6);
    expect(cluster.latitude).toBeGreaterThanOrEqual(cluster.bounds.south);
    expect(cluster.latitude).toBeLessThanOrEqual(cluster.bounds.north);
  });
});
