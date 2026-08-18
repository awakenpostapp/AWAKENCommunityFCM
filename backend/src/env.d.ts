// Secret bindings are intentionally absent from wrangler.jsonc. Wrangler typegen
// discovers non-secret bindings; this declaration augments the generated Env.
interface Env {
  JWT_SECRET: string;
  ADMIN_BOOTSTRAP_SECRET: string;
  SUPABASE_URL?: string;
  SUPABASE_SECRET_KEY?: string;
  DATA_BACKEND?: "d1" | "supabase";
  OAUTH_CALLBACK_URL?: string;
  GOOGLE_OAUTH_CLIENT_ID?: string;
  GOOGLE_OAUTH_CLIENT_SECRET?: string;
}
