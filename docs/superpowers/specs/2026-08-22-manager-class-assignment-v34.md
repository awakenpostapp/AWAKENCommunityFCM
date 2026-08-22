# Manager class assignment and Coach-required creation spec

## Goal

Enforce that only Founder and Co-Founder can create or structurally update classes, while Manager remains an operational/read-only class manager. Allow Founder/Co-Founder to optionally assign one Manager to a class, expose the assigned Manager in class information, and require at least one Coach whenever a class is created.

## Rules

- Manager cannot create a class from Android UI, direct Worker routes, or snapshot writes.
- Manager can still read assigned operational data and use the existing approval/account/finance workflows.
- Founder and Co-Founder can assign an active Manager from the same tenant to a class. The assignment is optional and is stored as `classes.manager_user_id`.
- A class creation must include at least one active Coach assignment and each Coach must belong to the same tenant.
- Existing legacy classes without a Coach remain readable; the Coach-required rule applies to new class creation.
- Class cards and detail pages show the assigned Manager when present.
- Cloudflare D1 and Supabase schemas receive additive migrations; existing rows and tenant data are preserved.
- Android release display version becomes `3.4`; build number increments from 117 to 118. Release APK is uploaded to GitHub Release; AAB and Debug are built locally only.
