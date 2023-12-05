export type GetSetProperty<T> = [T, (t:T) => void]

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
    year: number;
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


export interface Changeset {
    id: number;
    actionName: string;
    year: number;
    // cats: CategoryId[];
    changes: Changed[];
}

export interface PropertyValue {
    
}
export interface Property {
    id: number;
    value: {type: number; value: PropertyValue}
}
export interface Entity {
    id: number;
    type: number;
    properties: Property[];
}
export interface Changed {
    prev: Entity;
    new: Entity;
}
