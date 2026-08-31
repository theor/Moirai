// The entry point exists because a browser WebAssembly app has to be an Exe, not because there is any
// work to do here: the runtime starts, JavaScript calls MoiraiInterop.Load, and everything after that is
// driven from the worker. See Moirai.Wasm/Interop.cs.
System.Console.WriteLine("Moirai engine ready.");
