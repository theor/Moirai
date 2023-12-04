import * as signalR from "@microsoft/signalr";
import {HubConnection, HubConnectionState, IStreamResult} from "@microsoft/signalr";
import {ClientData, EntityPropertyDisplay, Message, MessageType, Record} from "./types.ts";
import {create} from "zustand";

export class SignalRConnection {
    public connection: HubConnection;

    private constructor(connection: HubConnection) {
        this.connection = connection;
    }

    static async make(): Promise<[SignalRConnection, ClientData, boolean]> {
        let connection = new signalR.HubConnectionBuilder()
            // .withUrl("http://localhost:5028/hub")
            // .withUrl("https://localhost:7148/hub")
            .withUrl("/hub")
            .configureLogging(signalR.LogLevel.Information)
            .build();
        // connection.onclose()
        connection.on("messageReceived", (username: string, message: string) => {
            console.log("messageReceived", username, message);
        });
        await connection.start();
        console.log("done", connection.state)
        let clientData = await connection.invoke("GetClientData")
        // console.log("data", clientData);
        return [new SignalRConnection(connection), clientData, connection.state === HubConnectionState.Connected];
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
// export const SignalRConnectionContext = createContext<{conn:SignalRConnection, data: [ClientData, (v:ClientData) => void], records: Record[]}>(null);
interface State {
    handleKeyPress: (this:Window, ev: KeyboardEvent) => any;
    keyboardEvent?: KeyboardEvent;
    clearEvent: () => void;
    year: number;
    connected: boolean;
    conn?: SignalRConnection;
    records: Record[];
    clientData?: ClientData;

    toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => void;
}

export const useMoiraiStore = create<State>((set, get) => {
    SignalRConnection.make().then(([x, y, c]) => {
        console.log("ZUSTAND done")
        x.connection.onreconnected(_id => {
            console.log("ZUS " + true)
            set({connected: true});
        });
        x.connection.onreconnecting(_id => {
            console.log("ZUS " + false)
            set({connected: false});
        });
        let buffer: Record[] = [];
        setInterval(() => {
            if (buffer.length > 0) {
                set({records: [...get().records, ...buffer]});
                buffer = []
            }
        }, 500);
        x.streamRecords().subscribe({
            next(value: Message) {
                switch (value.type) {
                    case MessageType.Reset:
                        set({year: 0, records: []})
                        break;
                    case MessageType.Record:
                        buffer.push(value.record!);
                        break;
                    case MessageType.Year:
                        set({year: value.year})
                        break;

                }
            },
            error(err: any) {
                console.error(err)
            },
            complete() {
            }
        })
        set({conn: x, clientData: y, connected: c});
    })
    return ({
        year: 0,
        connected: false,
        records: [],

        clearEvent: () => set({keyboardEvent: undefined}),
        handleKeyPress: e => {
            set({keyboardEvent: e});
        },
        
        toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => {
            const clientData = get().clientData!;
            if (switchAll) {
                set({
                    clientData: {
                        ...clientData, actions: clientData.actions.map(a => a.id === id ? {
                            ...a,
                            hidden: !active
                        } : {...a, hidden: active})
                    }
                });
                return;
            }
            clientData.actions[id - 1].hidden = !clientData.actions[id - 1].hidden;
            set({clientData: {...clientData}});
        }
    });
}); 
