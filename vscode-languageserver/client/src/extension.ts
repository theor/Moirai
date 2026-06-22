/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { copyFileSync, cpSync } from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { workspace, ExtensionContext, commands, RelativePattern } from "vscode";

import {
  ExecutableOptions,
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
  TransportKind,
} from "vscode-languageclient/node";

let client: LanguageClient;

export function activate(context: ExtensionContext) {
  // The server is implemented in node

  const exe = "Server.exe";
  let dirOrig = context.asAbsolutePath(path.join("client", "server"));
  let dirTarget = context.asAbsolutePath(path.join("client", "serverCopy"));
  let serverCommand = path.join(dirTarget, exe);
  let commandOptions: ExecutableOptions = { detached: false };
  const watchPath = path.join(dirOrig, exe);
  console.log("watching " + watchPath);

  const lspWatcher = workspace.createFileSystemWatcher(
    new RelativePattern(dirOrig, exe)
  );
  const onChange = debounce((e) => {
	console.log("Moirai LSP changed, restarting");
	restartHandler();
  }, 500);
  lspWatcher.onDidChange(onChange);

  cpSync(dirOrig, dirTarget, {
    errorOnExist: false,
    force: true,
    recursive: true,
  });
  // If the extension is launched in debug mode then the debug server options are used
  // Otherwise the run options are used
  const serverOptions: ServerOptions = {
    run: {
      command: serverCommand,
      transport: TransportKind.stdio,
      options: commandOptions,
    },
    debug: {
      command: serverCommand,
      transport: TransportKind.stdio,
      options: commandOptions,
    },
  };

  // Options to control the language client
  const clientOptions: LanguageClientOptions = {
    // Register the server for plain text documents
    documentSelector: [{ scheme: "file", language: "moirai" }],
    synchronize: {
      // Notify the server about file changes to '.clientrc files contained in the workspace
      fileEvents: workspace.createFileSystemWatcher("**/*.sg"),
    },
  };

  // Create the language client and start the client.
  client = new LanguageClient(
    "moiraiLanguageServer",
    "Moirai Language Server",
    serverOptions,
    clientOptions
  );

  const restartCommand = "moirai.restart";
  const restartHandler = async () => {
    console.log("Stopping Moirai LSP");
    await client.stop();
    await new Promise((resolve) => setTimeout(resolve, 1000));

    let done = false;
    for (let index = 0; index < 10 && !done; index++) {
      console.log("Copying Moirai LSP");
      try {
        cpSync(dirOrig, dirTarget, {
          errorOnExist: false,
          force: true,
          recursive: true,
        });
      } catch (error) {
        console.error(error);
        continue;
      }
      done = true;
    }
    console.log("Copy: " + (done ? "success" : "FAILED"));
    console.log("Starting Moirai LSP");
    client.start();
  };
  context.subscriptions.push(
    commands.registerCommand(restartCommand, restartHandler)
  );

  // Backs the "N usages" CodeLens overlay: the server can't hand VS Code typed Uri/Position/Location
  // objects through CodeLens command arguments, so it emits (uri, line, character) and we re-query
  // the reference provider here and open the peek view.
  context.subscriptions.push(
    commands.registerCommand(
      "moirai.showReferences",
      async (uriStr: string, line: number, character: number) => {
        const uri = vscode.Uri.parse(uriStr);
        const position = new vscode.Position(line, character);
        const locations =
          (await commands.executeCommand<vscode.Location[]>(
            "vscode.executeReferenceProvider",
            uri,
            position
          )) ?? [];
        if (locations.length === 0) {
          void vscode.window.showInformationMessage("No usages found");
          return;
        }
        await commands.executeCommand(
          "editor.action.showReferences",
          uri,
          position,
          locations
        );
      }
    )
  );

  registerDebugging(context);

  // Start the client. This will also launch the server
  client.start();
}

// Wire the Debug Adapter Protocol client. The adapter itself runs inside the Moirai web server
// (started with --debug-port), so we connect to it as a TCP server rather than spawning a process.
function registerDebugging(context: ExtensionContext) {
  const DEFAULT_PORT = 4711;
  const DEFAULT_HOST = "127.0.0.1";

  context.subscriptions.push(
    vscode.debug.registerDebugAdapterDescriptorFactory("moirai", {
      createDebugAdapterDescriptor(session) {
        const cfg = session.configuration;
        const port = typeof cfg.port === "number" ? cfg.port : DEFAULT_PORT;
        const host = typeof cfg.host === "string" ? cfg.host : DEFAULT_HOST;
        return new vscode.DebugAdapterServer(port, host);
      },
    })
  );

  // Provide a default config so F5 works in a .sg file with no launch.json.
  context.subscriptions.push(
    vscode.debug.registerDebugConfigurationProvider("moirai", {
      resolveDebugConfiguration(_folder, config) {
        if (!config.type && !config.request && !config.name) {
          config.type = "moirai";
          config.request = "launch";
          config.name = "Moirai: debug story";
          config.years = 100;
        }
        if (config.port === undefined) config.port = DEFAULT_PORT;
        if (config.host === undefined) config.host = DEFAULT_HOST;
        return config;
      },
    })
  );
}

export function deactivate(): Thenable<void> | undefined {
  if (!client) {
    return undefined;
  }
  return client.stop();
}

function debounce<Params extends any[]>(
  func: (...args: Params) => any,
  timeout: number
): (...args: Params) => void {
  let timer: NodeJS.Timeout;
  return (...args: Params) => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      func(...args);
    }, timeout);
  };
}
