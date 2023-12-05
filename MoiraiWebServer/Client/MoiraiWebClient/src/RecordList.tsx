import {GetSetProperty, Record} from "./types.ts";
import * as React from "react";
import TableCell from "@mui/material/TableCell";
// import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import {TableComponents, TableVirtuoso} from "react-virtuoso";
import TableRow from "@mui/material/TableRow";
import {makeEntityLink, useFiltering, useSelectedEntity} from "./utils.tsx";
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

function rowContent(_index: number, row: Record, [selectedEntity, filteredEntity]: [GetSetProperty<number>,GetSetProperty<number>]) {
    const text = makeEntityLink(row.text, selectedEntity, filteredEntity);
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

const RecordTable: TableComponents<Record, [GetSetProperty<number>, GetSetProperty<number>]> = {
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
    const [filteredEntity, setFilteredEntity] = useFiltering();
    const clientData = useMoiraiStore(s=>s.clientData!);
    // connState.
    console.log(filteredEntity, "F")
    const records = useMoiraiStore(s => s.records);
    const filteredRecords = records.filter(r => !clientData.actions[r.actionId-1].hidden && (filteredEntity === -1 || isNaN(filteredEntity) || r.text.indexOf(`#${filteredEntity}>`) !== -1));
    // const {conn, data:[clientData,_setClientData], records} = useContext(SignalRConnectionContext);
    return <TableVirtuoso
        context={[selectedEntity, [filteredEntity, setFilteredEntity]]}
        data={filteredRecords}
        components={RecordTable}
        fixedHeaderContent={fixedHeaderContent}
        itemContent={rowContent} followOutput={true}
    />;
}
