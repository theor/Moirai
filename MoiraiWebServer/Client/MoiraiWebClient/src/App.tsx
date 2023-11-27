// import {useEffect, useState} from 'react'

import './App.css'
import {useContext, useEffect, useState} from "react";
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
import {Box, Grid, Stack} from "@mui/material";

// import {HubConnection} from "@microsoft/signalr";


function RecordList() {
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
        <Stack spacing={2}>
            <Stack direction="row" spacing={1}>
                <Button variant="contained" onClick={() => context.passYears(100)}>Pass years</Button>
                <Button variant={"outlined"} onClick={() => {
                    context.reset();
                    setRecords([]);
                }}>Reset</Button>
            </Stack>
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
                        {records.map((r, i) => <TableRow key={i}>
                            <TableCell>{i}</TableCell>
                            <TableCell>{r.actionId} {context.clientData?.actions[r.actionId - 1].item2}</TableCell>
                            <TableCell>{r.year}</TableCell>
                            <TableCell
                                dangerouslySetInnerHTML={{__html: r.text.replace(/<#/g, "<a href=#").replace(/<\/>/g, "</a>")}}></TableCell>
                        </TableRow>)}
                    </TableBody>
                </Table>
            </TableContainer>
        </Stack>
    </>;
}

function App() {
    const [conn, setConn] = useState<SignalRConnection | null>(null);

    useEffect(() => {
        SignalRConnection.make().then(setConn)
    }, []);

    return conn ? (
        <SignalRConnectionContext.Provider value={conn}>
            <Grid spacing={1} mt={2}>
                <Container maxWidth="lg">
                    <RecordList/>
                </Container>
            </Grid>
        </SignalRConnectionContext.Provider>
    ) : <span>Loading</span>
}

export default App
