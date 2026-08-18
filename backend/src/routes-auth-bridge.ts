import { getUserBundle } from "./repository";
import { publicClub, publicProfile, publicUser, UserRow } from "./domain";

/** Shared response shaping for password/OAuth/Supabase Auth exchanges. */
export async function authBundle(env: Env, user: UserRow, tokens?: object): Promise<Record<string, unknown>> {
  const bundle = await getUserBundle(env, user.id);
  return {
    ...(tokens ?? {}),
    user: publicUser(user),
    profile: publicProfile(bundle.profile),
    activeClub: publicClub(bundle.club),
    club: publicClub(bundle.club),
  };
}
