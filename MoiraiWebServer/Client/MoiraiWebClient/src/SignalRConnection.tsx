import * as signalR from "@microsoft/signalr";
import {HubConnection, IStreamResult} from "@microsoft/signalr";
import {createContext} from "react";
import {Record, ClientData} from "./types.ts";

export class SignalRConnection {
    public connection: HubConnection;
    public clientData: ClientData;

    private constructor(connection: HubConnection, clientData: ClientData) {
        this.connection = connection;
        this.clientData = clientData;
    }

    static async make(): Promise<SignalRConnection> {
        let connection = new signalR.HubConnectionBuilder()
            // .withUrl("http://localhost:5028/hub")
            // .withUrl("https://localhost:7148/hub")
            .withUrl("/hub")
            .configureLogging(signalR.LogLevel.Trace)
            .build();
        // connection.onclose()
        connection.on("messageReceived", (username: string, message: string) => {
            console.log(username, message);
        });
        await connection.start();
        console.log("done", connection.state)
        let data: ClientData = await connection.invoke("GetClientData")
        console.log("data", data);
        return new SignalRConnection(connection, data);
        // connection.send("newMessage", "theoir", "test")


    }

    reset(): void {
        this.connection.send("Reset")

    }

    passYears(years: number): void {
        this.connection.send("PassYears", years)
    }


    streamRecords(): IStreamResult<Record> {
        return this.connection.stream<Record>("Counter", 20, 100)
    }
}
// @ts-ignore
export const SignalRConnectionContext = createContext<SignalRConnection>(null);

