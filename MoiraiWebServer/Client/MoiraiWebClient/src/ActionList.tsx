import {
    Divider,
    Switch,
    TableRow,
    Typography
} from "@mui/material";
import {useContext, useState} from "react";
import {SignalRConnectionContext} from "./SignalRConnection.tsx";
import Table from "@mui/material/Table";
import TableContainer from "@mui/material/TableContainer";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import {ActionData} from "./types.ts";

export function ActionList(){
    const ctx = useContext(SignalRConnectionContext)
    const [checked, setChecked] = useState<number[]>([]);
    const handleToggle = (value: ActionData) => (e:React.MouseEvent<HTMLButtonElement>) => {
        const currentIndex = checked.indexOf(value.id);

        console.log(e)
        if(e.ctrlKey)
        {
            setChecked([...ctx.clientData.actions.map(a => a.id).filter(i => i !== value.id)])
            return;
        }
        const newChecked = [...checked];

        if (currentIndex === -1) {
            newChecked.push(value.id);
        } else {
            newChecked.splice(currentIndex, 1);
        }

        setChecked(newChecked);
    };
    return <>
        <Divider/>
        <Typography gutterBottom mt={2}
                    variant="h5">Actions</Typography>
        <TableContainer sx={{  overflow: 'auto' }} >
        <Table  sx={{overflow:"auto"}} size="small">
            <TableBody>
                {ctx.clientData.actions.map(a => {
                    return <TableRow key={a.id}>
                        <TableCell >
                            <Switch size="small" checked={checked.indexOf(a.id) == -1} onClick={handleToggle(a)} />
                            <span>
                            {a.name}
                                </span>
                        </TableCell>
                    </TableRow>
                })}
            </TableBody>
        </Table>
    </TableContainer>
    </>
    // return <List dense  sx={{ width: '100%', height: '45vh', overflow: 'auto' }} >
    //     {ctx.clientData.actions.map(a => {
    //         const labelId = `checkbox-list-label-${a.id}`;
    //         return <ListItem key={a.id} disablePadding>
    //             <ListItemButton role={undefined} onClick={handleToggle(a.id)} dense>
    //                 <ListItemIcon>
    //                     <Checkbox
    //                         edge="start"
    //                         size={"small"}
    //                         checked={checked.indexOf(a.id) !== -1}
    //                         tabIndex={-1}
    //                         disableRipple
    //                         inputProps={{'aria-labelledby': labelId}}
    //                     />
    //                 </ListItemIcon>
    //                 <ListItemText id={labelId} primary={a.name}/>
    //             </ListItemButton>
    //         </ListItem>;
    //     })}
    // </List>;
}
