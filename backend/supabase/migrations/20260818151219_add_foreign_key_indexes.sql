-- Add direct indexes for foreign-key columns that are not the leading
-- column of an existing tenant-scoped composite index. This keeps deletes,
-- integrity checks, and role-scoped joins predictable as the clubs grow.

CREATE INDEX IF NOT EXISTS idx_attendance_recorded_by_user
  ON public.attendance_records (recorded_by_user_id);
CREATE INDEX IF NOT EXISTS idx_attendance_trainee_user
  ON public.attendance_records (trainee_user_id);
CREATE INDEX IF NOT EXISTS idx_audit_actor_user
  ON public.audit_logs (actor_user_id);
CREATE INDEX IF NOT EXISTS idx_class_coaches_coach_user
  ON public.class_coaches (coach_user_id);
CREATE INDEX IF NOT EXISTS idx_class_enrollments_trainee_user
  ON public.class_enrollments (trainee_user_id);
CREATE INDEX IF NOT EXISTS idx_classes_venue
  ON public.classes (venue_id);
CREATE INDEX IF NOT EXISTS idx_coach_checkins_coach_user
  ON public.coach_checkins (coach_user_id);
CREATE INDEX IF NOT EXISTS idx_coach_checkins_reviewed_by_user
  ON public.coach_checkins (reviewed_by_user_id);
CREATE INDEX IF NOT EXISTS idx_coach_salaries_paid_by_user
  ON public.coach_salaries (paid_by_user_id);
CREATE INDEX IF NOT EXISTS idx_coach_salaries_tenant
  ON public.coach_salaries (tenant_id);
CREATE INDEX IF NOT EXISTS idx_idempotency_keys_tenant
  ON public.idempotency_keys (tenant_id);
CREATE INDEX IF NOT EXISTS idx_notifications_tenant
  ON public.notifications (tenant_id);
CREATE INDEX IF NOT EXISTS idx_payment_proofs_invoice
  ON public.payment_proofs (invoice_id);
CREATE INDEX IF NOT EXISTS idx_payment_proofs_reviewed_by_user
  ON public.payment_proofs (reviewed_by_user_id);
CREATE INDEX IF NOT EXISTS idx_receipts_tenant
  ON public.receipts (tenant_id);
CREATE INDEX IF NOT EXISTS idx_session_coaches_coach_user
  ON public.session_coaches (coach_user_id);
CREATE INDEX IF NOT EXISTS idx_sync_cursors_tenant
  ON public.sync_cursors (tenant_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_class
  ON public.trainee_evaluations (class_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_coach_user
  ON public.trainee_evaluations (coach_user_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_reviewed_by_user
  ON public.trainee_evaluations (reviewed_by_user_id);
CREATE INDEX IF NOT EXISTS idx_evaluations_trainee_user
  ON public.trainee_evaluations (trainee_user_id);
CREATE INDEX IF NOT EXISTS idx_training_sessions_submitted_by_user
  ON public.training_sessions (submitted_by_user_id);
CREATE INDEX IF NOT EXISTS idx_tuition_invoices_class
  ON public.tuition_invoices (class_id);
CREATE INDEX IF NOT EXISTS idx_tuition_invoices_trainee_user
  ON public.tuition_invoices (trainee_user_id);
