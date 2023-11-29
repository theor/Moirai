import * as signalR from "@microsoft/signalr";
import {HubConnection, IStreamResult} from "@microsoft/signalr";
import {createContext} from "react";
import { Message, ClientData, EntityPropertyDisplay} from "./types.ts";

export class SignalRConnection {
    public connection: HubConnection;

    private constructor(connection: HubConnection) {
        this.connection = connection;
    }

    static async make(): Promise<[SignalRConnection, ClientData]> {
        let connection = new signalR.HubConnectionBuilder()
            // .withUrl("http://localhost:5028/hub")
            // .withUrl("https://localhost:7148/hub")
            .withUrl("/hub")
            .configureLogging(signalR.LogLevel.Information)
            .build();
        // connection.onclose()
        connection.on("messageReceived", (username: string, message: string) => {
            console.log(username, message);
        });
        await connection.start();
        console.log("done", connection.state)
        let clientData = await connection.invoke("GetClientData")
        // console.log("data", clientData);
        return [new SignalRConnection(connection), clientData];
        // connection.send("newMessage", "theoir", "test")


    }

    reset() {
        return this.connection.send("Reset")

    }

    passYears(years: number) {
        return this.connection.send("PassYears", years)
    }
    
    save() {
        return this.connection.send("Save");
    }


    streamRecords(): IStreamResult<Message> {
        return this.connection.stream<Message>("Stream")
    }

    getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]> {
        return this.connection.invoke("GetEntityDetails", entityId)
    }
}
// @ts-ignore
export const SignalRConnectionContext = createContext<{conn:SignalRConnection, data: [ClientData, (v:ClientData) => void]}>(null);

