import {GetSetProperty} from "./types.ts";
import * as React from "react";
import {Chip, Tooltip} from "@mui/material";
import {useSearchParams} from "react-router-dom";

export function makeEntityLink(str: string, selectedEntity: GetSetProperty<number>, filteredEntity: GetSetProperty<number>): React.JSX.Element[] {
    const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/ig;
    return [...str.matchAll(rx)].map((match: RegExpMatchArray, i) => {
        if (!match?.groups)
            return <span key={i}>???</span>;
        if (match.groups["text"]) {
            return <span key={i}>{match.groups["text"]}</span>;
        } else {
            let id: number = Number(match.groups["id"]);
            return <Tooltip title={"#" + id}
                            key={i}>{makeEntityChip(id, match.groups["link"], selectedEntity, filteredEntity)}</Tooltip>
        }
    });
}
export function makeEntityChip(id: number, label: string, selectedEntity: GetSetProperty<number>, filteredEntity: GetSetProperty<number>) {
    return <Chip size="small" color="primary"
                 variant={selectedEntity[0] == id ? "filled" : "outlined"} clickable
                 onClick={() => selectedEntity[1](id)}
                 onDoubleClick={() => filteredEntity[1](id)}
                 label={label}/>;
}

export function useSelectedEntity(): GetSetProperty<number>{
    let [searchParams, setSearchParams] = useSearchParams({eid:'0'});
    let eid = Number(searchParams.get("eid"));
    return [eid, (x:number) => setSearchParams((p:URLSearchParams) => {
        p.set("eid", x.toString());
        return p;
    })];
}
// export enum Filtering {
//     None,
//     Entity,
// }
export function useFiltering(): GetSetProperty<number>{
    let [searchParams, setSearchParams] = useSearchParams({f:'-1'});
    let eid = Number(searchParams.get("f") ?? -1);
    return [eid, (x:number) => setSearchParams((p:URLSearchParams) => {
        p.set("f", x.toString());
        return p;
    })];
}
export function useYearsDelta(): GetSetProperty<number>{
    let [searchParams, setSearchParams] = useSearchParams({delta:'10'});
    let eid = Number(searchParams.get("delta") ?? 10);
    return [eid, (x:number) => setSearchParams((p:URLSearchParams) => {
        p.set("delta", x.toString());
        return p;
    })];
}

export function useMainListDisplay() : GetSetProperty<boolean> {
    let [searchParams, setSearchParams] = useSearchParams({showChangesets:String(false)});
    let eid = (searchParams.get("showChangesets") === 'true');
    return [eid, (x:boolean) => setSearchParams((p:URLSearchParams) => {
        console.log("show", x);
        p.set("showChangesets", x.toString());
        return p;
    })];
}
