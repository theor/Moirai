import * as signalR from "@microsoft/signalr";
import {HubConnection, HubConnectionState, IStreamResult, IStreamSubscriber} from "@microsoft/signalr";
import {ClientData, EntityPropertyDisplay, Message, MessageType, Record} from "./types.ts";
import {create} from "zustand";
export interface Result {
    eid: number;
    properties: EntityPropertyDisplay[];
}

export interface FamilyTreeNode {
    id: number;
    name: string;
    p1: number;
    p2: number;
}
export interface QueryResult
{
    sql: string ;
    query: string ;
    results: Result[] ;
    errors: string[] ;
}
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

    runAction(actionId: number) {
        return this.connection.send("RunAction", actionId);
    }
    
    getChangesets(): IStreamResult<EntityChangeDisplay> {
        return this.connection.stream<EntityChangeDisplay>("GetChangesets");
    }

    async reset() {
        return this.connection.invoke<number>("Reset")
    }
    
    query(q:string): Promise<QueryResult> {
        return this.connection.invoke<QueryResult>("Query", q);
    }

    passYears(years: number): IStreamResult<number> {
        return this.connection.stream<number>("PassYears", years)
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
    getFamilyTree(entityId: number, maxDepth: number): Promise<FamilyTreeNode[]> {
        return this.connection.invoke("GetFamilyTree", entityId, maxDepth)
    }
}

// @ts-ignore
// export const SignalRConnectionContext = createContext<{conn:SignalRConnection, data: [ClientData, (v:ClientData) => void], records: Record[]}>(null);
interface State {
    reset: () => void;
    pushChangesets: (buffer: EntityChangeDisplay[]) => void;
    handleKeyPress: (this:Window, ev: KeyboardEvent) => any;
    keyboardEvent?: KeyboardEvent;
    clearEvent: () => void;
    year: number;
    connected: boolean;
    conn?: SignalRConnection;
    records: Record[];
    changesets: EntityChangeDisplay[];
    clientData?: ClientData;
    passYears: (amount:number) => void;
    passYearsProgress?: number;

    toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => void;
}
export interface EntityChangeDisplay {
    id: number;
    year: number;
    actionName: string;
    changes: EntityPropertyDisplay[];
}
let _targetYear: number = 0;
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
                // console.log("BUFFER RECORDs")
                set({records: [...get().records, ...buffer]});
                buffer = []
            }
        }, 500);
        x.streamRecords().subscribe({
            next(value: Message) {
                // console.log(value, value.type === MessageType.Record);
                switch (value.type) {
                    case MessageType.Reset:
                        if(value.year !== 0) {
                            console.warn(`RESET, fast forward to ${value.year}, current year is ${get().year}`)
                            _targetYear = value.year;
                        }
                        set({year: 0, records: []})
                        break;
                    case MessageType.Record:
                        buffer.push(value.record!);
                        break;
                    case MessageType.Year:
                        set({year: value.year})
                        if(_targetYear !== 0) {
                            try {
                                console.log("fast forward to ", _targetYear);
                                get().passYears(_targetYear - value.year);
                            }
                            finally {
                                _targetYear = 0;
                            }
                        }
                        break;
                    default: console.error("UNKNOWN MESSAGE TYPE", value.type)
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
        passYearsProgress: 0,
        connected: false,
        records: [],
        changesets: [],

        
        passYears: (amount: number) => {
            const subscriber: IStreamSubscriber<number> = {
                next(value: number) {
                    set({passYearsProgress: value})
                },
                error(err: any) {
                    console.error(err)
                    set({passYearsProgress: undefined})
                },
                complete() {
                    console.log("PROGRESS COMPLETE");
                    set({passYearsProgress: undefined})
                }
            };
            set({passYearsProgress: 0});
            return get().conn!.passYears(amount).subscribe(subscriber);
        },
        reset: async () => {
            const newYear = await get().conn!.reset();
            console.warn("newyear reset", newYear);
            set({year: newYear, records: []});
        },
        clearEvent: () => set({keyboardEvent: undefined}),
        handleKeyPress: e => {
            set({keyboardEvent: e});
        },
        pushChangesets: (buffer: EntityChangeDisplay[]) =>  {
            set({changesets:  [...buffer]})
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
