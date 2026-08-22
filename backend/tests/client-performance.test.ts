import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const root = new URL("../../", import.meta.url);
const databaseSource = await readFile(new URL("Services/AppDatabase.cs", root), "utf8");
const snapshotSource = await readFile(new URL("backend/src/snapshot.ts", root), "utf8");

test("online projection refreshes use conditional sync versions", () => {
  assert.match(
    databaseSource,
    /GetSnapshotAsync\(\s*Online\.SyncVersion\s*>\s*0\s*\?\s*Online\.SyncVersion\s*:\s*null\s*\)/u,
  );
  assert.match(databaseSource, /wireSnapshot\.Unchanged[\s\S]*Online\.MarkFresh\(wireSnapshot\.SyncVersion\)/u);
  assert.match(databaseSource, /QueueCloudProjectionRefresh[\s\S]*Online\.InvalidateData\(\)/u);
});

test("online mode does not initialize the legacy SQLite cache", () => {
  const onlineBranch = databaseSource.match(
    /public async Task InitializeAsync\(\)[\s\S]*?if \(_initialized\)/u,
  )?.[0] ?? "";
  assert.match(onlineBranch, /if \(IsOnline\)[\s\S]*?_initialized\s*=\s*true[\s\S]*?return;/u);
  assert.doesNotMatch(onlineBranch, /new SQLiteAsyncConnection/iu);
});

test("snapshot endpoint supports a no-change response", () => {
  assert.match(snapshotSource, /afterSyncVersion\s*&&\s*afterSyncVersion\s*===\s*syncVersion/u);
  assert.match(snapshotSource, /unchanged:\s*true/u);
});
