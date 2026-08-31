import * as signalR from '@microsoft/signalr';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import type { MoiraiApi, MoiraiApiHandle, MoiraiStream } from './api';
import type {
  Biography,
  ClientData,
  EntityChangeDisplay,
  EntityPropertyDisplay,
  FamilyTreeNode,
  Message,
  QueryResult,
  RuleCoverageReport,
  TimeSeries,
  WorldOverview,
} from './types';

/**
 * {@link MoiraiApi} over a SignalR hub — the .NET host's transport. Each method is one hub call; the
 * server unwraps it onto the same `WorldSession` the WebAssembly build drives directly.
 */
export class SignalRApi implements MoiraiApi {
  public connection: HubConnection;

  private constructor(connection: HubConnection) {
    this.connection = connection;
  }

  static async make(): Promise<MoiraiApiHandle> {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hub')
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    await connection.start();
    const clientData: ClientData = await connection.invoke('GetClientData');
    return {
      api: new SignalRApi(connection),
      clientData,
      connected: connection.state === HubConnectionState.Connected,
    };
  }

  onConnectedChanged(handler: (connected: boolean) => void) {
    this.connection.onreconnected(() => handler(true));
    this.connection.onreconnecting(() => handler(false));
    this.connection.onclose(() => handler(false));
  }

  runAction(actionId: number): Promise<void> {
    return this.connection.send('RunAction', actionId);
  }

  getChangesets(start: number, count: number): Promise<EntityChangeDisplay[]> {
    return this.connection.invoke('GetChangesets', start, count);
  }

  getEntityChangesets(entityId: number): Promise<EntityChangeDisplay[]> {
    return this.connection.invoke('GetEntityChangesets', entityId);
  }

  reset(): Promise<number> {
    return this.connection.invoke<number>('Reset');
  }

  reseed(seed: number): Promise<number> {
    return this.connection.invoke<number>('Reseed', seed);
  }

  query(q: string): Promise<QueryResult> {
    return this.connection.invoke<QueryResult>('Query', q);
  }

  passYears(years: number): MoiraiStream<number> {
    return this.connection.stream<number>('PassYears', years);
  }

  save(): Promise<void> {
    return this.connection.send('Save');
  }

  streamRecords(): MoiraiStream<Message> {
    return this.connection.stream<Message>('Stream');
  }

  getBiography(entityId: number): Promise<Biography> {
    return this.connection.invoke<Biography>('GetBiography', entityId);
  }

  getWorldOverview(): Promise<WorldOverview> {
    return this.connection.invoke<WorldOverview>('GetWorldOverview');
  }

  getPropertySeries(typeId: number, propertyName: string): Promise<TimeSeries> {
    return this.connection.invoke<TimeSeries>('GetPropertySeries', typeId, propertyName);
  }

  getRuleCoverage(): Promise<RuleCoverageReport> {
    return this.connection.invoke<RuleCoverageReport>('GetRuleCoverage');
  }

  getEntityDetails(entityId: number): Promise<EntityPropertyDisplay[]> {
    return this.connection.invoke('GetEntityDetails', entityId);
  }

  getFamilyTree(entityId: number, maxDepth: number): Promise<FamilyTreeNode[]> {
    return this.connection.invoke('GetFamilyTree', entityId, maxDepth);
  }
}
