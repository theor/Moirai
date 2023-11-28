import './App.css'
import {useContext, useEffect, useState} from "react";
import Button from '@mui/material/Button';
import {SignalRConnection, SignalRConnectionContext} from "./SignalRConnection.tsx";
import {Grid, IconButton, Stack, Typography} from "@mui/material";
import {Outlet, Route, Routes} from 'react-router-dom';
import {RecordList} from "./RecordList.tsx";
import {ActionList} from "./ActionList.tsx";
import {EntityDetails} from "./EntityDetails.tsx";
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Toolbar from '@mui/material/Toolbar';
import MenuIcon from '@mui/icons-material/Menu';

function InnerApp() {
    const conn = useContext(SignalRConnectionContext);
    return <>
        <Box>
            <AppBar position="static">
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
        </Box>
        <Box sx={{flexGrow: 1}} display="grid" gridTemplateColumns="repeat(3, 1fr)" gridTemplateRows="0.5fr 0.5fr"
             gap={2} py={2}>
            <Box>
                <Outlet/>
            </Box>
            <Box gridColumn="span 2" gridRow="span 2">
                <RecordList/>
            </Box>
            <Box>
                <ActionList/>
            </Box>
        </Box>
    </>
}

function App() {
    const [conn, setConn] = useState<SignalRConnection | null>(null);

    useEffect(() => {
        if (!conn)
            SignalRConnection.make().then(setConn)
    }, [conn]);

    return conn ? (
            <SignalRConnectionContext.Provider value={conn}>
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
