import { Outlet } from "react-router-dom";
import Sidebar from "../components/layout/Sidebar";

export default function MainLayout() {
    return (
        <div className="flex min-h-screen flex-col bg-slate-100 lg:flex-row">

            <Sidebar />

            <main className="min-w-0 flex-1">

                <header className="flex min-h-16 items-center border-b bg-white px-4 py-3 shadow-sm sm:px-8">

                    <div>

                        <h2 className="text-2xl font-semibold text-slate-800">
                            Dashboard
                        </h2>

                        <p className="text-sm text-slate-500">
                            COOP-AI Smart Cooperative Platform
                        </p>

                    </div>

                </header>

                <section className="p-4 sm:p-6 lg:p-8">
                    <Outlet />
                </section>

            </main>

        </div>
    );
}
