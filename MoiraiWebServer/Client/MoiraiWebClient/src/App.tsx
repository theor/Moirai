// import {useEffect, useState} from 'react'

import './App.css'
import Container from '@mui/material/Container';
import Typography from '@mui/material/Typography';
import * as signalR from "@microsoft/signalr";
import {useEffect, useState} from "react";
import {HubConnection} from "@microsoft/signalr";
import Button from '@mui/material/Button';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Paper from '@mui/material/Paper';
// import {HubConnection} from "@microsoft/signalr";
const connection = new signalR.HubConnectionBuilder()
    // .withUrl("http://localhost:5028/hub")
    // .withUrl("https://localhost:7148/hub")
    .withUrl("/hub")
    .configureLogging(signalR.LogLevel.Trace)
    .build();
connection.on("messageReceived", (username: string, message: string) => {
    console.log(username, message);
});

interface Record {
    text: string;
    changesetId: number;
    actionId: number;
    year: number;
    categories: number;
}

interface ClientData {
    actions: any[];
}
function App() {
    const [clientData, setClientData] = useState<ClientData|null>(null);
    const [conn, setConn] = useState<HubConnection|null>(null);
    const [records, setRecords] = useState<Record[]>([]);
    useEffect(() => {
        connection.start().then(async () => {
            console.log("done", connection.state)
            let data: ClientData = await connection.invoke("GetClientData")
            console.log("data", data);
            setClientData(data);
            setConn(connection);
            // connection.send("newMessage", "theoir", "test")
        }).catch((err) => document.write(err))
    }, []);
    useEffect(() => {
        if(!clientData)   return;
        const stream = connection.stream<Record>("Counter", 20, 100).subscribe({
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
    }, [clientData]);
    return (
        <Container maxWidth="lg">
            <TableContainer component={Paper}>
                <Table stickyHeader={true} aria-label="simple table">
                <TableHead>
                <TableRow>
                    <TableCell >id</TableCell >
                    <TableCell >action</TableCell >
                    <TableCell >year</TableCell >
                    <TableCell >text</TableCell >
                </TableRow>
                </TableHead>
                <TableBody>
                {records.map((r,i) => <TableRow key={i}>
                    <TableCell >{i}</TableCell >
                    <TableCell >{r.actionId} {clientData?.actions[r.actionId - 1].item2}</TableCell >
                    <TableCell >{r.year}</TableCell >
                    <TableCell  dangerouslySetInnerHTML={{__html: r.text.replaceAll("<#", "<a href=#").replaceAll("</>", "</a>")}}></TableCell >
                </TableRow>)}
                </TableBody>
                </Table>
            </TableContainer>
            <Button onClick={() => conn.send("PassYears", 100)}>Pass years</Button>
        </Container>
    )
}

export default App
