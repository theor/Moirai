import { Property, Record} from "./types.ts";
import * as React from "react";
import TableCell from "@mui/material/TableCell";
// import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import {TableComponents, TableVirtuoso} from "react-virtuoso";
import TableRow from "@mui/material/TableRow";
import {makeEntityLink, useSelectedEntity} from "./utils.tsx";
import TableContainer from "@mui/material/TableContainer";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableHead from "@mui/material/TableHead";
import TableBody from "@mui/material/TableBody";
import {useMoiraiStore} from "./SignalRConnection.tsx";

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
    // connState.
    const filteredRecords = useMoiraiStore(s => s.records);
    // const {conn, data:[clientData,_setClientData], records} = useContext(SignalRConnectionContext);
    return <TableVirtuoso
        context={selectedEntity}
        data={filteredRecords}
        components={RecordTable}
        fixedHeaderContent={fixedHeaderContent}
        itemContent={rowContent} followOutput={true}
    />;
}
