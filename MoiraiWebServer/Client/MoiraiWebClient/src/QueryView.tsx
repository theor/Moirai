import Box from "@mui/material/Box";
import {Table, TextField} from "@mui/material";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableRow from "@mui/material/TableRow";
import {useState} from "react";
import debounce from 'lodash/debounce';
import {Result, useMoiraiStore} from "./SignalRConnection.tsx";
export function QueryView(){
    // const [query, setQuery] = useState("pick Person p")
    const [results, setResults] = useState<Result[]>([])
    const onChange = debounce(async (x:string) => {
        console.log(x);
        const res = await conn.query(x);
        setResults(res);
    }, 500);
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
        <TableContainer sx={{overflow: 'auto'}}>
        
        <Table>
            <TableBody>
                {results.length == 0 ? <TableRow><TableCell>No results</TableCell></TableRow> : results.map((r,i) => <TableRow key={i}>
                    <TableCell>{r.eid}</TableCell>
                    <TableCell>{r.description}</TableCell>
                </TableRow>)}
            </TableBody>
        </Table>
        </TableContainer>
    </Box>
}
