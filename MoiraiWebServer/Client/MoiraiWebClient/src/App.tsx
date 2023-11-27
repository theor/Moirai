// import {useEffect, useState} from 'react'

import './App.css'
import * as React from 'react';
import {Dispatch, SetStateAction, useContext, useEffect, useState} from "react";
import Container from '@mui/material/Container';
import Button from '@mui/material/Button';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Paper from '@mui/material/Paper';
import {SignalRConnection, SignalRConnectionContext} from "./SignalRConnection.tsx";
import {Box, Card, CardContent, Chip, Grid, Stack, Typography} from "@mui/material";
import {TableVirtuoso, TableComponents} from 'react-virtuoso';
import {EntityPropertyDisplay, Property, Record} from "./types.ts";

// import {HubConnection} from "@microsoft/signalr";


interface RecordListProps {
    selectedEntity: Property<number>
}

const RecordTable: TableComponents<Record> = {
    Scroller: React.forwardRef<HTMLDivElement>((props, ref) => (
        <TableContainer  component={Paper} {...props} ref={ref}/>
    )),
    Table: (props) => (
        <Table {...props} sx={{borderCollapse: 'separate', tableLayout: 'fixed'}}/>
    ),
    TableHead,
    TableRow: ({item: _item, ...props}) => <TableRow {...props} />,
    TableBody: React.forwardRef<HTMLTableSectionElement>((props, ref) => (
        <TableBody {...props} ref={ref}/>
    )),
};

interface ColumnData {
    dataKey: keyof Record;
    label: string;
    numeric?: boolean;
    width?: number;
}

const columns: ColumnData[] = [
    // {
    //     width: 200,
    //     label: 'Id',
    //     dataKey: 'id',
    // },
    {
        width: 60,
        label: 'Year',
        dataKey: 'year',
        numeric: true,
    },
    {
        label: 'Text',
        dataKey: 'text',
    },

];

function fixedHeaderContent() {
    return (
        <TableRow>
            {columns.map((column) => (
                <TableCell
                    key={column.dataKey}
                    variant="head"
                    align={column.numeric || false ? 'right' : 'left'}
                    style={{width: column.width ?? "auto"}}
                    sx={{
                        backgroundColor: 'background.paper',
                    }}
                >
                    {column.label}
                </TableCell>
            ))}
        </TableRow>
    );
}
function makeEntityLink(str: string, selectedEntity: Property<number>): React.JSX.Element[] {
    const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/ig;
    const text = [...str.matchAll(rx)].map((match: RegExpMatchArray, i) => {
        if (!match?.groups)
            return <span key={i}>???</span>;
        if (match.groups["text"]) {
            return <span key={i}>{match.groups["text"]}</span>;
        } else {
            let id: number = Number(match.groups["id"]);
            return <React.Fragment key={i}>{makeEntityChip(id, match.groups["link"], selectedEntity)}</React.Fragment>
        }
    })
    return text;
}
function makeEntityChip(id: number, label: string, selectedEntity: Property<number>) {
   return <Chip size="small" color="primary"
          variant={selectedEntity[0] == id ? "filled" : "outlined"} clickable
          onClick={() => selectedEntity[1](id)}
          label={label}/>;
}
function rowContent(_index: number, row: Record, selectedEntity: Property<number>) {
   const text = makeEntityLink(row.text, selectedEntity);
    return (
        <React.Fragment>
            {columns.map((column) => (
                <TableCell
                    key={column.dataKey}
                    align={column.numeric || false ? 'right' : 'left'}
                >

                    {column.dataKey == "text" ? text : row[column.dataKey]}
                </TableCell>
            ))}
        </React.Fragment>
    );
}

function RecordList({selectedEntity}: RecordListProps) {
    const [records, setRecords] = useState<Record[]>([]);
    const context = useContext(SignalRConnectionContext);
    useEffect(() => {
        const stream = context.streamRecords().subscribe({
            next(i) {
                setRecords(records => [...records, i])
                // console.log(i, records)
            },
            complete() {
                console.log("complete")
            },
            error: (err) => console.error(err),

        });
        return () => {
            setRecords([])
            stream.dispose();
        };
    }, [context]);
    return <>
        <TableVirtuoso
            context={selectedEntity}
            data={records}
            components={RecordTable}
            fixedHeaderContent={fixedHeaderContent}
            itemContent={rowContent}
        />
        {/*<TableContainer sx={{maxHeight: "90vh"}} component={Paper}>*/}
        {/*    <Table stickyHeader={true} aria-label="simple table" size={"small"}>*/}
        {/*        <TableHead>*/}
        {/*            <TableRow>*/}
        {/*                <TableCell>id</TableCell>*/}
        {/*                <TableCell>action</TableCell>*/}
        {/*                <TableCell>year</TableCell>*/}
        {/*                <TableCell>text</TableCell>*/}
        {/*            </TableRow>*/}
        {/*        </TableHead>*/}
        {/*        <TableBody>*/}
        {/*            {records.map((r, i) => {*/}
        {/*                const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/ig;*/}
        {/*                const text = [...r.text.matchAll(rx)].map((match: RegExpMatchArray, i) => {*/}
        {/*                    if (!match?.groups)*/}
        {/*                        return <span key={i}>???</span>;*/}
        {/*                    if (match.groups["text"]) {*/}
        {/*                        return <span key={i}>{match.groups["text"]}</span>;*/}
        {/*                    } else {*/}
        {/*                        let id: number = Number(match.groups["id"]);*/}
        {/*                        return <Chip key={i} size="small" color="primary"*/}
        {/*                                     variant={selectedEntity[0] == id ? "filled" : "outlined"} clickable*/}
        {/*                                     onClick={() => selectedEntity[1](id)}*/}
        {/*                                     label={match.groups["link"]}/>;*/}
        {/*                    }*/}
        {/*                })*/}
        {/*                return <TableRow key={i}>*/}
        {/*                    <TableCell>{i}</TableCell>*/}
        {/*                    <TableCell>{r.actionId} {context.clientData?.actions[r.actionId - 1].item2}</TableCell>*/}
        {/*                    <TableCell>{r.year}</TableCell>*/}
        {/*                    <TableCell>*/}
        {/*                        {text}*/}
        {/*                    </TableCell>*/}
        {/*                </TableRow>;*/}
        {/*            })}*/}
        {/*        </TableBody>*/}
        {/*    </Table>*/}
        {/*</TableContainer>*/}
    </>;
}


function EntityDetails({selectedEntity}:RecordListProps) {
    var ctx = useContext(SignalRConnectionContext);
    var [details, setDetails] = useState<EntityPropertyDisplay[]>([]);
    useEffect(() => {
        console.log("sel changed", selectedEntity[0])
        if(selectedEntity[0] != -1)
        ctx.getEntityDetails(selectedEntity[0]).then(setDetails);
    }, [selectedEntity[0]]);
    return <Card sx={{height:"100%"}} variant="outlined" >
        <CardContent>
    <Typography gutterBottom variant="h5">{selectedEntity[0] !== -1 ? "Entity #" + selectedEntity[0] : "-"}</Typography>
            <TableContainer component={Paper}>
                <TableBody>
                    {details.map((d,i) =>
                        <TableRow key={i}>
                            <TableCell>{d.label}</TableCell>
                            <TableCell>{makeEntityLink(d.value, selectedEntity)}</TableCell>
                        </TableRow>)}
                </TableBody>
            </TableContainer>
            {/*{details.map((d,i) => */}
            {/*<Typography key={i}>*/}
            {/*    {d.label}: {d.value}*/}
            {/*</Typography>)}*/}
        </CardContent>
        </Card>
}
function App() {
    const [conn, setConn] = useState<SignalRConnection | null>(null);
    const selectedEntity= useState<number>(-1);

    useEffect(() => {
        if (!conn)
            SignalRConnection.make().then(setConn)
    }, [conn]);

    return conn ? (
            <SignalRConnectionContext.Provider value={conn}>
                <Grid container height="100vh"  pt={2} pb={2}>
                    <Grid item xs={4} p={2}>
                        <EntityDetails selectedEntity={selectedEntity}/>
                    </Grid>
                    <Grid item xs={8} p={2}>
                        <Stack spacing={2} sx={{height: "100%"}}>
                            <Stack direction="row" spacing={1}>
                                <Button variant="contained" onClick={() => conn.passYears(100)}>Pass years</Button>
                                <Button variant={"outlined"} onClick={() => {
                                    conn.reset();
                                    setConn(null)
                                }}>Reset</Button>
                            </Stack>
                            <RecordList selectedEntity={selectedEntity}/>
                        </Stack>
                    </Grid>
                </Grid>
            </SignalRConnectionContext.Provider>
        ) :
        <span>Loading</span>
}

export default App
