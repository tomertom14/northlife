-- LOCAL DEVELOPMENT ONLY. Removes the demo catalogue and ALL analytics data (raw interactions,
-- hourly and daily statistics, popularity) so `--seed-demo` can run again from a clean slate.
-- Accounts, real events and images are kept.
--   Get-Content scripts/reset-demo-data.sql | docker exec -i northlife-postgres-1 psql -U northlife -d northlife
BEGIN;
TRUNCATE interactions, event_stats_hourly, event_stats_daily, event_popularity;
UPDATE analytics_checkpoints SET processed_until_utc = now();
DELETE FROM events WHERE owner_id IN (SELECT id FROM users WHERE email LIKE '%@demo.northlife.local');
-- Places before images and owners (both are referenced); their opening hours go with them.
DELETE FROM places WHERE owner_id IN (SELECT id FROM users WHERE email LIKE '%@demo.northlife.local');
DELETE FROM event_images WHERE uploader_id IN (SELECT id FROM users WHERE email LIKE '%@demo.northlife.local');
DELETE FROM users WHERE email LIKE '%@demo.northlife.local';
COMMIT;
