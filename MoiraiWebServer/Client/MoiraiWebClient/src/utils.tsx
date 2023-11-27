import {Property} from "./types.ts";
import * as React from "react";
import {Chip, Tooltip} from "@mui/material";
import {useNavigate, useParams} from "react-router-dom";

export function makeEntityLink(str: string, selectedEntity: Property<number>): React.JSX.Element[] {
    const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/ig;
    const text = [...str.matchAll(rx)].map((match: RegExpMatchArray, i) => {
        if (!match?.groups)
            return <span key={i}>???</span>;
        if (match.groups["text"]) {
            return <span key={i}>{match.groups["text"]}</span>;
        } else {
            let id: number = Number(match.groups["id"]);
            return <Tooltip title={"#"+id} key={i}>{makeEntityChip(id, match.groups["link"], selectedEntity)}</Tooltip>
        }
    })
    return text;
}
export function makeEntityChip(id: number, label: string, selectedEntity: Property<number>) {
    return <Chip size="small" color="primary"
                 variant={selectedEntity[0] == id ? "filled" : "outlined"} clickable
                 onClick={() => selectedEntity[1](id)}
                 label={label}/>;
}

export function useSelectedEntity(): Property<number>{
    let eid = Number(useParams().eid);
    var nav = useNavigate();
    return [eid, (x:number) => nav("/entity/" + x)];

}
