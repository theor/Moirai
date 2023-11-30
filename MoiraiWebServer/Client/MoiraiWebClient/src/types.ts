export type Property<T> = [T, (t:T) => void]

export interface Record {
    text: string;
    changesetId: number;
    actionId: number;
    year: number;
    categories: number;
}

export interface Message {
    type: MessageType;
    record: Record | null;
}

export enum MessageType {
    Reset,
    Record,
    Year,
}

export type EntityPropertyDisplay = {label: string, value: string}

export interface ActionData { id:number;  name: string; hidden: boolean; }
export interface ClientData {
    actions: ActionData[];
}
