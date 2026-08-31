// Boot script for the Moirai WebAssembly engine.
//
// Runs on the page's main thread. That is not a preference: the single-threaded .NET runtime only
// finishes initialising there — inside a dedicated worker it downloads every assembly, reaches preInit
// and then never completes, with no error to show for it. The host therefore keeps the page responsive by
// simulating in short chunks and yielding between them (see ClientAppSvelte/src/lib/wasm-api.ts).
import { dotnet } from './dotnet.js';

export async function boot() {
  const { getAssemblyExports, getConfig, runMain } = await dotnet.create();

  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);

  // Runs the (trivial) managed entry point. Skipping it leaves the runtime in a state where some
  // startup work has not happened, so do it before any export is called.
  await runMain(config.mainAssemblyName, []);

  return exports.Moirai.Wasm.MoiraiInterop;
}
