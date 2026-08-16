ALTER TABLE tenants ADD COLUMN founder_status TEXT NOT NULL DEFAULT 'approved'
  CHECK (founder_status IN ('pending', 'approved', 'disabled'));

-- Existing suspended tenants were created by the original approval flow. Keep
-- them in the Admin approval queue; future disables are explicitly marked
-- disabled by the status endpoint.
UPDATE tenants
SET founder_status = 'pending'
WHERE status = 'suspended';
