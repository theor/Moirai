import {makeEntityLink, useSelectedEntity} from "./utils.tsx";
import {useContext, useEffect, useState} from "react";
import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import {EntityPropertyDisplay} from "./types.ts";
import {Card, CardContent, Typography} from "@mui/material";
import TableContainer from "@mui/material/TableContainer";
import Paper from "@mui/material/Paper";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableRow from "@mui/material/TableRow";
import TableCell from "@mui/material/TableCell";

export function EntityDetails() {
    let selectedEntity = useSelectedEntity();
    let ctx = useContext(SignalRConnectionContext);
    let [details, setDetails] = useState<EntityPropertyDisplay[]>([]);
    useEffect(() => {
        console.log("sel changed", selectedEntity[0])
        if (selectedEntity[0] != -1)
            ctx.getEntityDetails(selectedEntity[0]).then(setDetails);
    }, [selectedEntity[0]]);
    return <Card sx={{height: "100%"}} variant="outlined">
        <CardContent>
            <Typography gutterBottom
                        variant="h5">{selectedEntity[0] !== -1 ? "Entity #" + selectedEntity[0] : "-"}</Typography>
            <TableContainer component={Paper}>
                <Table>
                    <TableBody>
                        {details.map((d, i) =>
                            <TableRow key={i}>
                                <TableCell>{d.label}</TableCell>
                                <TableCell>{makeEntityLink(d.value, selectedEntity)}</TableCell>
                            </TableRow>)}
                    </TableBody>
                </Table>
            </TableContainer>
            {/*{details.map((d,i) => */}
            {/*<Typography key={i}>*/}
            {/*    {d.label}: {d.value}*/}
            {/*</Typography>)}*/}
        </CardContent>
    </Card>
}
