/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */

import { copyFileSync } from 'fs';
import * as path from 'path';
import { workspace, ExtensionContext } from 'vscode';

import {
	ExecutableOptions,
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {
	// The server is implemented in node
	let serverCommandOrig = context.asAbsolutePath(path.join('client','server', 'Server.exe'));
	let serverCommand = context.asAbsolutePath(path.join('client','server', 'Server2.exe'));
	let commandOptions: ExecutableOptions = {  detached: false };
	copyFileSync(serverCommandOrig, serverCommand)
	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	const serverOptions: ServerOptions = {
		run: { command: serverCommand, transport: TransportKind.stdio, options: commandOptions },
		debug: {
			command: serverCommand,
			transport: TransportKind.stdio, options: commandOptions 
		}
	};

	// Options to control the language client
	const clientOptions: LanguageClientOptions = {
		// Register the server for plain text documents
		documentSelector: [{ scheme: 'file', language: 'moirai' }],
		synchronize: {
			// Notify the server about file changes to '.clientrc files contained in the workspace
			fileEvents: workspace.createFileSystemWatcher('**/.clientrc')
		}
	};

	// Create the language client and start the client.
	client = new LanguageClient(
		'moiraiLanguageServer',
		'Moirai Language Server',
		serverOptions,
		clientOptions
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