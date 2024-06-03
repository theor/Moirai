/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { copyFileSync, cpSync } from "fs";
import * as path from "path";
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

  // Start the client. This will also launch the server
  client.start();
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
