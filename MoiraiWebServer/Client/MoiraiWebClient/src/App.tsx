// import {useEffect, useState} from 'react'

import './App.css'
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
import {Box, Chip, Grid, Stack} from "@mui/material";
import { TableVirtuoso, TableComponents } from 'react-virtuoso';

// import {HubConnection} from "@microsoft/signalr";


interface RecordListProps {
    selectedEntity: Property<number>
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

        <TableContainer sx={{maxHeight: "90vh"}} component={Paper}>
            <Table stickyHeader={true} aria-label="simple table" size={"small"}>
                <TableHead>
                    <TableRow>
                        <TableCell>id</TableCell>
                        <TableCell>action</TableCell>
                        <TableCell>year</TableCell>
                        <TableCell>text</TableCell>
                    </TableRow>
                </TableHead>
                <TableBody>
                    {records.map((r, i) => {
                        const rx = /(?:(?:<#(?<id>\d+)>(?<link>[^<]+)<\/>)|(?<text>[^<\n]+))/ig;
                        const text = [...r.text.matchAll(rx)].map((match: RegExpMatchArray, i) => {
                            if (!match?.groups)
                                return <span key={i}>???</span>;
                            if (match.groups["text"]) {
                                return <span key={i}>{match.groups["text"]}</span>;
                            } else {
                                let id: number = Number(match.groups["id"]);
                                return <Chip key={i} size="small" color="primary"
                                             variant={selectedEntity[0] == id ? "filled" : "outlined"} clickable
                                             onClick={() => selectedEntity[1](id)}
                                             label={match.groups["link"]}/>;
                            }
                        })
                        return <TableRow key={i}>
                            <TableCell>{i}</TableCell>
                            <TableCell>{r.actionId} {context.clientData?.actions[r.actionId - 1].item2}</TableCell>
                            <TableCell>{r.year}</TableCell>
                            <TableCell>
                                {text}
                            </TableCell>
                        </TableRow>;
                    })}
                </TableBody>
            </Table>
        </TableContainer>
    </>;
}

type Property<T> = [T, Dispatch<SetStateAction<T>>]

function App() {
    const [conn, setConn] = useState<SignalRConnection | null>(null);
    const [selectedEntity, setSelectedEntity] = useState<number>(-1);

    useEffect(() => {
        if (!conn)
            SignalRConnection.make().then(setConn)
    }, [conn]);

    return conn ? (
            <SignalRConnectionContext.Provider value={conn}>
                <Container maxWidth="lg">
                    <Grid container spacing={2} mt={2}>
                        <Grid item xs={4}></Grid>
                        <Grid item xs={8}>
                            <Stack spacing={2}>
                                <Stack direction="row" spacing={1}>
                                    <Button variant="contained" onClick={() => conn.passYears(100)}>Pass years</Button>
                                    <Button variant={"outlined"} onClick={() => {
                                        conn.reset();
                                        setConn(null)
                                    }}>Reset</Button>
                                </Stack>
                                <RecordList selectedEntity={[selectedEntity, setSelectedEntity]}/>
                            </Stack>
                        </Grid>
                    </Grid>
                </Container>
            </SignalRConnectionContext.Provider>
        ) :
        <span>Loading</span>
}

export default App
