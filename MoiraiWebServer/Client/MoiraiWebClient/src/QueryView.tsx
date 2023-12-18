import Box from "@mui/material/Box";
import {Alert, Table, TextField} from "@mui/material";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableRow from "@mui/material/TableRow";
import {useState} from "react";
import debounce from 'lodash/debounce';
import {QueryResult, useMoiraiStore} from "./SignalRConnection.tsx";
import {makeEntityChip, useFiltering, useSelectedEntity} from "./utils.tsx";
import * as _ from "lodash";
export function QueryView(){
    // const [query, setQuery] = useState("pick Person p")
    const [results, setResults] = useState<QueryResult|undefined>()
    const onChange = debounce(async (x:string) => {
        console.log(x);
        const res = await conn.query(x);
        setResults(res);
    }, 500);
    const selectedEntity = useSelectedEntity();
    const filteredEntity = useFiltering();
    const conn = useMoiraiStore(s => s.conn!);
    return <Box sx={{width:"100%"}}>
        <TextField
            sx={{width:"100%"}}
            id="outlined-multiline-static"
            label="Multiline"
            multiline
            rows={4}
            defaultValue={'pick Person p'}
            onChange={e => onChange(e.target.value)}
            // value={query}
            // onChange={(e:React.ChangeEvent<HTMLInputElement>) => setQuery(e.target.value)}
        />
        {results?.sql && <Box p={2}><pre>{results.sql}</pre></Box>}
        {results?.errors && results.errors.map((e,i) => <Box py={2}><Alert severity="error" key={i}>{e}</Alert></Box>)}
        <TableContainer sx={{overflow: 'auto'}}>
        
        <Table>
            <TableBody>
                {(results?.results?.length ?? 0) == 0 ? <TableRow><TableCell>No results</TableCell></TableRow> : _.take(results!.results,20).map((r,i) => <TableRow key={i}>
                    <TableCell>{makeEntityChip(r.eid, r.eid.toString(), selectedEntity, filteredEntity)}</TableCell>
                    {/*<TableCell>{makeEntityLink(r.description, selectedEntity, filteredEntity)}</TableCell>*/}
                    {r.properties.map((p,pi) => <TableCell key={pi}>{p.label} {p.value}</TableCell>)}
                </TableRow>)}
            </TableBody>
        </Table>
        </TableContainer>
    </Box>
}
