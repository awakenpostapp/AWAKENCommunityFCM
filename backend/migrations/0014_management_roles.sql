-- Production reads and writes use Supabase; its role CHECK is expanded by
-- supabase/migrations/20260820100000_management_roles.sql. The populated D1
-- rollback copy has foreign-key references from every operational table to
-- users, and Cloudflare D1 forbids writable_schema/constraint rewrites. Keep
-- this numbered migration as a safe, data-preserving marker instead of a
-- destructive users-table rebuild. The role vocabulary remains documented so
-- schema checks cannot silently drop admin, founder, co_founder, manager,
-- coach, or trainee.
SELECT 1 AS management_roles_migration_noop;
