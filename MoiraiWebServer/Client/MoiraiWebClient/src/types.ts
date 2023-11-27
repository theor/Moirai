import {Dispatch, SetStateAction} from "react";

export type Property<T> = [T, Dispatch<SetStateAction<T>>]

export interface Record {
    text: string;
    changesetId: number;
    actionId: number;
    year: number;
    categories: number;
}

export type EntityPropertyDisplay = {label: string, value: string}
export interface ClientData {
    actions: any[];
}
