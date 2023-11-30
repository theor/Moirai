import './App.css'
import {useContext, useEffect, useState} from "react";
import {SignalRConnection, SignalRConnectionContext} from "./SignalRConnection.tsx";
import {Outlet, Route, Routes} from 'react-router-dom';
import {RecordList} from "./RecordList.tsx";
import {EventList} from "./EventList.tsx";
import {EntityDetails} from "./EntityDetails.tsx";
import Box from '@mui/material/Box';
import {AppBar, Grid, Stack, Toolbar,IconButton, Typography, Button} from "@mui/material";
import MenuIcon from '@mui/icons-material/Menu';
import {ClientData, Record} from "./types.ts";
function InnerApp() {
    const {conn} = useContext(SignalRConnectionContext);
    return <>
        {/*<Box>*/}
            <AppBar position="relative" sx={{marginBottom:"12px"}}>
                <Toolbar>
                    <IconButton
                        size="large"
                        edge="start"
                        color="inherit"
                        aria-label="menu"
                        sx={{mr: 2}}
                    >
                        <MenuIcon/>
                    </IconButton>
                    <Typography variant="h6" component="div" sx={{flexGrow: 1}}/>
                    <Typography variant="h6" component="div">
                        Year: 123
                    </Typography>
                    <Button color="inherit" onClick={() => conn.passYears(100)}>Pass years</Button>
                    <Button color="inherit" onClick={() => conn.save()}>Save</Button>
                    <Button color="inherit" onClick={() => {
                        conn.reset();
                    }}>Reset</Button>
                </Toolbar>
            </AppBar>
        {/*</Box>*/}
        <Grid container spacing={2} sx={{flexGrow: 1, height: "100%"}} mb={8} px={2}>
            <Grid item xs={4} sx={{height:"100%"}}>
                <Stack gap={2} sx={{height:"95%"}} direction={"column"} >
                    <Box>
                        <Outlet/>
                    </Box>
                    <Box  sx={{overflow:"auto"}}>
                        <EventList/>
                        {/*<Outlet/>*/}
                    </Box>
                </Stack>
            </Grid>
            <Grid item xs={8} sx={{paddingBottom:"64px"}}>
                <RecordList/>
            </Grid>
        </Grid>
        {/*<Box sx={{flexGrow: 1}} display="grid" gridTemplateColumns="repeat(3, 1fr)" gridAutoRows="50%"*/}
        {/*     gap={2} py={2} px={2}>*/}
        {/*    <Box>*/}
        {/*        <Outlet/>*/}
        {/*    </Box>*/}
        {/*    <Box gridColumn="span 2" gridRow="span 2">*/}
        {/*        <RecordList/>*/}
        {/*    </Box>*/}
        {/*    <Box>*/}
        {/*        /!*<Outlet/>*!/*/}
        {/*        <ActionList/>*/}
        {/*    </Box>*/}
        {/*</Box>*/}

    </>
}

function App() {
    const [clientData, setClientData] = useState<ClientData>();
    const [conn, setConn] = useState<SignalRConnection | null>(null);
    const [records, setRecords] = useState<Record[]>([]);
    
    useEffect(() => {
        if (!conn)
            SignalRConnection.make().then(([conn,data]) => {setConn(conn); setClientData(data)})
    }, [conn]);

    return conn && clientData ? (
            <SignalRConnectionContext.Provider value={{conn:conn, data: [clientData, setClientData], records}}>
                <Routes>
                    <Route path="/" element={<InnerApp/>}>
                        <Route path="entity/:eid" element={<EntityDetails/>}/>
                    </Route>
                </Routes>

            </SignalRConnectionContext.Provider>
        ) :
        <span>Loading</span>
}

export default App
