-- Showcase Event Management (reference SQL)
-- Prefer EF EnsureCreated + DbSeeder for local demo.
-- Room codes: A-001, B-102, C-201 (Building-LevelRoom)

IF DB_ID(N'FypEventManagement') IS NULL
    CREATE DATABASE FypEventManagement;
GO
USE FypEventManagement;
GO

-- See backend-api EF model for full schema.
-- Buildings A/B/C, 4 floors (0-3), 20 rooms each are seeded by the API.
