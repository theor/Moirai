import { redirect } from '@sveltejs/kit';

// The app has no standalone home view; send visitors straight to the records feed.
export function load() {
  throw redirect(307, '/records');
}
