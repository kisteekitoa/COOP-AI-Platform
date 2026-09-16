import { BrowserRouter, Routes, Route } from "react-router-dom";

import ProtectedRoute from "../auth/ProtectedRoute";
import MainLayout from "../layouts/MainLayout";
import DashboardPage from "../pages/DashboardPage";
import DebtSegmentationPage from "../pages/DebtSegmentationPage";
import ImportPage from "../pages/ImportPage";
import LoginPage from "../pages/LoginPage";
import PortfolioSnapshotsPage from "../pages/PortfolioSnapshotsPage";

export default function AppRouter() {
    return (
        <BrowserRouter>

            <Routes>
                <Route path="/login" element={<LoginPage />} />

                <Route element={<ProtectedRoute />}>
                    <Route element={<MainLayout />}>

                    <Route
                        path="/"
                        element={<DashboardPage />}
                    />
                    <Route
                        path="/import"
                        element={<ImportPage />}
                    />
                    <Route
                        path="/portfolio-snapshots"
                        element={<PortfolioSnapshotsPage />}
                    />
                    <Route
                        path="/portfolio-snapshots/:id"
                        element={<PortfolioSnapshotsPage />}
                    />
                    <Route
                        path="/debt-segmentation"
                        element={<DebtSegmentationPage />}
                    />
                    <Route
                        path="/work-queue"
                        element={<DebtSegmentationPage />}
                    />

                    </Route>
                </Route>

            </Routes>

        </BrowserRouter>
    );
}
