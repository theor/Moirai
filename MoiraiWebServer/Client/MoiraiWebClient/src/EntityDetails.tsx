import {makeEntityLink, useFiltering, useSelectedEntity} from "./utils.tsx";
import {Fragment, useEffect, useState} from "react";
import {useMoiraiStore} from "./SignalRConnection.tsx";
import {EntityPropertyDisplay} from "./types.ts";
import {
    Divider,
    ToggleButton,//Box, List, ListItem, ListItemButton, ListItemIcon, ListItemText, 
    Typography, useMediaQuery
} from "@mui/material";
import Box from "@mui/material/Box";
import FilterAlt from '@mui/icons-material/FilterAlt';
export function EntityDetails() {
    let selectedEntity = useSelectedEntity();
    let filtering = useFiltering();
    const conn = useMoiraiStore(s => s.conn!);
    let [details, setDetails] = useState<EntityPropertyDisplay[]>([]);
    useEffect(() => {
        // console.log("sel changed", selectedEntity[0])
        if (selectedEntity[0] != -1)
            conn.getEntityDetails(selectedEntity[0]).then(setDetails);
    }, [selectedEntity[0]]);
    const matches = useMediaQuery('(min-width:700px)');
    return <>
        <Box sx={{display: "flex",flexDirection:"row"}}>
            <Typography gutterBottom sx={{flex:1}}
                        variant="h5">{selectedEntity[0] !== -1 ? "Entity #" + selectedEntity[0] : "-"}
            </Typography>
        <ToggleButton size="small" selected={filtering[0] !== -1}  onChange={() => filtering[1](filtering[0] === -1 ? selectedEntity[0] : -1)}  value={"filtered"} aria-label="search"><FilterAlt/></ToggleButton>
        </Box>
        
        {/*<Box sx={{ width: '100%', maxWidth: 360, bgcolor: 'background.paper' }}>*/}
        {/*    <nav aria-label="main mailbox folders">*/}
        {/*        <List dense component={Paper}>*/}
        {/*           */}
        {/*                {details.map((d, i) =>*/}
        {/*                    <ListItem key={i} disablePadding>*/}
        {/*                    <ListItemButton>*/}
        {/*                        <ListItemIcon><Typography sx={{fontSize: "0.8rem"}} fontWeight="bold" >{d.label.toUpperCase()}</Typography> </ListItemIcon>*/}
        {/*                        <ListItemText primary={makeEntityLink(d.value, selectedEntity)} />*/}
        {/*                    </ListItemButton>*/}
        {/*                    </ListItem>*/}
        {/*                        */}
        {/*                        )}*/}
        {/*                */}
        {/*         */}
        {/*        </List>*/}
        {/*    </nav>*/}
        {/*</Box>*/}

        <Box display="grid" gap={1} sx={{maxHeight: "50vh", overflowY: "auto", overflowX:"none"}}
             gridTemplateRows='auto' gridTemplateColumns={ matches ? "1fr 2fr" : "1fr"}>
            {details.map((d, i) => <Fragment key={i}>
                <Box >
                    <Divider sx={{display:matches ? "none" : "inherit"}}/>
                    <Typography align={!matches ?  "left" : "right"} fontWeight="bold" >{d.label.toUpperCase()}</Typography>
                </Box>
                    <Box >{makeEntityLink(d.value, selectedEntity, filtering)}</Box>
                </Fragment>
            )}
        </Box>
        
        
        {/*<Grid container columns={{ sm:1,md:1, lg:2}} spacing={1}>*/}
        {/*    {details.map((d, i) => <>*/}
        {/*        <Grid item xs={1} key={i + 'l'} sx={{alignContent:"right"}}><Typography align={"right"} fontWeight="bold" >{d.label.toUpperCase()}</Typography> </Grid>*/}
        {/*        <Grid item xs={1} key={i + 'v'}>{makeEntityLink(d.value, selectedEntity)}</Grid>*/}
        {/*        </>*/}
        {/*    )}*/}
        {/*</Grid>*/}
            {/*<TableContainer sx={{ overflow: "auto"}} component={Paper}>*/}
            {/*    <Table size="small">*/}
            {/*        <TableBody>*/}
            {/*            {details.map((d, i) =>*/}
            {/*                <TableRow key={i}>*/}
            {/*                    <TableCell sx={{paddingRight: 0}} align={"right"}><Typography sx={{fontSize: "0.8rem"}} fontWeight="bold" >{d.label.toUpperCase()}</Typography> </TableCell>*/}
            {/*                    <TableCell sx={{*/}
            {/*                        whiteSpace: 'normal',*/}
            {/*                        wordWrap: 'break-word'*/}
            {/*                    }}>{makeEntityLink(d.value, selectedEntity)}</TableCell>*/}
            {/*                </TableRow>)}*/}
            {/*        </TableBody>*/}
            {/*    </Table>*/}
            {/*</TableContainer>*/}
    </>
}
