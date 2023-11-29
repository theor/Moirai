import {MessageType, Property, Record} from "./types.ts";
import * as React from "react";
import TableCell from "@mui/material/TableCell";
import {useContext, useEffect, useState} from "react";
import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import {TableComponents, TableVirtuoso} from "react-virtuoso";
import TableRow from "@mui/material/TableRow";
import {makeEntityLink, useSelectedEntity} from "./utils.tsx";
import TableContainer from "@mui/material/TableContainer";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableHead from "@mui/material/TableHead";
import TableBody from "@mui/material/TableBody";

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

const RecordTable: TableComponents<Record, Property<number>> = {
    Scroller: React.forwardRef<HTMLDivElement>((props, ref) => (
        <TableContainer  component={Paper} {...props} ref={ref}/>
    )),
    Table: (props) => (
        <Table size="small" {...props} sx={{borderCollapse: 'separate', tableLayout: 'fixed'}}/>
    ),
    TableHead,
    TableRow: ({item: _item, ...props}) => <TableRow {...props} />,
    TableBody: React.forwardRef<HTMLTableSectionElement>((props, ref) => (
        <TableBody {...props} ref={ref}/>
    )),
};

export function RecordList() {
    const selectedEntity = useSelectedEntity();
    const [records, setRecords] = useState<Record[]>([]);
    const context = useContext(SignalRConnectionContext);
    useEffect(() => {
        console.log("stream")
        const stream = context.streamRecords().subscribe({
            next(i) {
                switch(i.type)
                {
                    case MessageType.Reset:
                        setRecords([]);
                        break;
                    case MessageType.Record:
                        setRecords(records => [...records, i.record!])
                        break;

                }
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
    useEffect(() => {
        console.log("CDATA")
    }, [context.clientData]);
    return <TableVirtuoso
        context={selectedEntity}
        data={records}
        components={RecordTable}
        fixedHeaderContent={fixedHeaderContent}
        itemContent={rowContent}
    />;
}
