import { BrowserRouter, Routes, Route } from "react-router-dom";

import MainLayout from "../layouts/MainLayout";
import DashboardPage from "../pages/DashboardPage";
import ImportPage from "../pages/ImportPage";

export default function AppRouter() {
    return (
        <BrowserRouter>

            <Routes>

                <Route element={<MainLayout />}>

                    <Route
                        path="/"
                        element={<DashboardPage />}
                    />
                    <Route
                        path="/import"
                        element={<ImportPage />}
                    />

                </Route>

            </Routes>

        </BrowserRouter>
    );
}