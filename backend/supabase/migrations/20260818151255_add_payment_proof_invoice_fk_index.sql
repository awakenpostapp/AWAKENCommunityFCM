-- The existing tenant-scoped index has a different name and cannot cover
-- the invoice foreign-key check without tenant as the leading column.
CREATE INDEX IF NOT EXISTS idx_payment_proofs_invoice_fk
  ON public.payment_proofs (invoice_id);
