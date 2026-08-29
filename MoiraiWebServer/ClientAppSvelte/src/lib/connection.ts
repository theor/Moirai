import * as signalR from '@microsoft/signalr';
import {
  HubConnection,
  HubConnectionState,
  type IStreamResult,
  type IStreamSubscriber,
} from '@microsoft/signalr';
import {
  type ClientData,
  type EntityPropertyDisplay,
  type Message,
  MessageType,
  type Record,
} from './types';
import { get, writable } from 'svelte/store';
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
export interface QueryResult {
  sql: string;
  query: string;
  results: Result[];
  errors: string[];
}
export class SignalRConnection {
  public connection: HubConnection;

  private constructor(connection: HubConnection) {
    this.connection = connection;
  }

  static async make(): Promise<[SignalRConnection, ClientData, boolean]> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub')
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    await connection.start();
    const clientData = await connection.invoke('GetClientData');
    return [
      new SignalRConnection(connection),
      clientData,
      connection.state === HubConnectionState.Connected,
    ];
  }

  runAction(actionId: number) {
    return this.connection.send('RunAction', actionId);
  }

  getChangesets(start: number, count: number): Promise<EntityChangeDisplay[]> {
    return this.connection.invoke('GetChangesets', start, count);
  }

  getEntityChangesets(entityId: number): Promise<EntityChangeDisplay[]> {
    return this.connection.invoke('GetEntityChangesets', entityId);
  }

  async reset() {
    return this.connection.invoke<number>('Reset');
  }

  query(q: string): Promise<QueryResult> {
    return this.connection.invoke<QueryResult>('Query', q);
  }

  passYears(years: number): IStreamResult<number> {
    return this.connection.stream<number>('PassYears', years);
  }

  save() {
    return this.connection.send('Save');
  }

  streamRecords(): IStreamResult<Message> {
    return this.connection.stream<Message>('Stream');
  }

  getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]> {
    return this.connection.invoke('GetEntityDetails', entityId);
  }
  getFamilyTree(entityId: number, maxDepth: number): Promise<FamilyTreeNode[]> {
    return this.connection.invoke('GetFamilyTree', entityId, maxDepth);
  }
}

interface State {
  year: number;
  connected: boolean;
  conn?: SignalRConnection;
  records: Record[];
  changesets: EntityChangeDisplay[];
  clientData?: ClientData;
  passYearsProgress?: number;
  // toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => void;
}
export interface EntityChangeDisplay {
  id: number;
  year: number;
  actionName: string;
  changes: EntityPropertyDisplay[];
}
let _targetYear: number = 0;
const writableStore = writable<State>(
  {
    year: 0,
    passYearsProgress: undefined,
    connected: false,
    records: [],
    changesets: [],
  },
  (set, update) => {
    SignalRConnection.make().then(([x, clientData, c]) => {
      x.connection.onreconnected((_id) => {
        update((x) => ({ ...x, connected: true }));
      });
      x.connection.onreconnecting((_id) => {
        update((x) => ({ ...x, connected: false }));
      });

      // setup record streaming
      let buffer: Record[] = [];
      setInterval(() => {
        if (buffer.length > 0) {
          update((x) => ({ ...x, records: [...x.records, ...buffer] }));
          buffer = [];
        }
      }, 500);

      x.streamRecords().subscribe({
        next(value: Message) {
          switch (value.type) {
            case MessageType.Reset:
              if (value.year !== 0) {
                update((x) => {
                  _targetYear = value.year;
                  return x;
                });
              }
              update((x) => ({ ...x, year: 0, records: [] }));
              break;
            case MessageType.Record:
              buffer.push(value.record!);
              break;
            case MessageType.Year:
              if (value.year !== get(writableStore).year) {
                update((x) => ({ ...x, year: value.year }));
              }
              if (_targetYear !== 0) {
                _targetYear = 0;
              }
              break;
            default:
              console.error('UNKNOWN MESSAGE TYPE', value.type);
          }
        },
        error(err: unknown) {
          console.error(err);
        },
        complete() {},
      });

      update((s) => ({ ...s, conn: x, connected: c, clientData }));
    });
  },
);

export const moiraiStore = {
  ...writableStore,
  reset: async () => {
    const newYear = await get(writableStore).conn!.reset();
    writableStore.update((x) => ({ ...x, year: newYear, records: [] }));
  },
  passYears: (amount: number) => {
    const subscriber: IStreamSubscriber<number> = {
      next(value: number) {
        writableStore.update((x) => ({ ...x, passYearsProgress: value }));
      },
      error(err: unknown) {
        console.error(err);
        writableStore.update((x) => ({ ...x, passYearsProgress: undefined }));
      },
      complete() {
        writableStore.update((x) => ({ ...x, passYearsProgress: undefined }));
      },
    };
    writableStore.update((x) => ({ ...x, passYearsProgress: 0 }));
    return get(writableStore).conn!.passYears(amount).subscribe(subscriber);
  },
  clearEvent: () => writableStore.update((x) => ({ ...x, keyboardEvent: undefined })),
  handleKeyPress: (e: KeyboardEvent): void => {
    writableStore.update((x) => ({ ...x, keyboardEvent: e }));
  },
  toggleActionFiltering: (id: number, active: boolean, switchAll: boolean) => {
    const clientData = get(writableStore).clientData!;
    if (switchAll) {
      writableStore.update((x) => ({
        ...x,
        clientData: {
          ...clientData,
          actions: clientData.actions.map((a) =>
            a.id === id ? { ...a, hidden: !active } : { ...a, hidden: active },
          ),
        },
      }));
      return;
    }
    clientData.actions[id - 1].hidden = !clientData.actions[id - 1].hidden;
    writableStore.update((x) => ({ ...x, clientData: { ...clientData } }));
  },
  getChangesets: (start: number, count: number) => {
    return get(writableStore).conn!.getChangesets(start, count);
  },
};
