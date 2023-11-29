import * as signalR from "@microsoft/signalr";
import {HubConnection, IStreamResult} from "@microsoft/signalr";
import {createContext} from "react";
import { Message, ClientData, EntityPropertyDisplay} from "./types.ts";

export class SignalRConnection {
    public connection: HubConnection;
    public clientData: ClientData;
    public setClientData: (value: ClientData) => void;

    private constructor(connection: HubConnection, clientData: ClientData, setClientData: (value:ClientData) => void) {
        this.connection = connection;
        this.clientData = clientData;
        this.setClientData = setClientData;
        if(clientData)
            setClientData(clientData);
    }

    static async make(clientData: ClientData | undefined, setClientData: (value: (((prevState: (ClientData | undefined)) => (ClientData | undefined)) | ClientData | undefined)) => void): Promise<SignalRConnection> {
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
        clientData = await connection.invoke("GetClientData")
        console.log("data", clientData);
        return new SignalRConnection(connection, clientData!, setClientData);
        // connection.send("newMessage", "theoir", "test")


    }

    reset(): void {
        this.connection.send("Reset")

    }

    passYears(years: number): void {
        this.connection.send("PassYears", years)
    }
    
    save() {
        this.connection.send("Save");
    }


    streamRecords(): IStreamResult<Message> {
        return this.connection.stream<Message>("Stream")
    }

    getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]> {
        return this.connection.invoke("GetEntityDetails", entityId)
    }
}
// @ts-ignore
export const SignalRConnectionContext = createContext<SignalRConnection>(null);

