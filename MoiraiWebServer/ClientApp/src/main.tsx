import ReactDOM from 'react-dom/client'
import App from './App.tsx'
import './index.css'
import { ThemeProvider } from '@emotion/react';
import { CssBaseline } from '@mui/material';
import theme from './theme';

import {
    createBrowserRouter,
    RouterProvider,
} from "react-router-dom";


const router = createBrowserRouter([
    {
        path: "*",
        element: <App/>,
    },
]);
// const defaultTheme = createTheme();

ReactDOM.createRoot(document.getElementById('root')!).render(
  // <React.StrictMode>
    <ThemeProvider theme={theme}>

        <CssBaseline />
        <RouterProvider router={router} />
    </ThemeProvider>
  // </React.StrictMode>,
)
