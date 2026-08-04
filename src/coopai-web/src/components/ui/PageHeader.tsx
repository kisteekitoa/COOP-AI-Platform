type Props = {
    title: string;
    subtitle?: string;
};

export default function PageHeader({
    title,
    subtitle,
}: Props) {

    return (

        <div className="mb-6">

            <h1 className="text-3xl font-bold text-slate-800">

                {title}

            </h1>

            {subtitle && (

                <p className="text-slate-500 mt-1">

                    {subtitle}

                </p>

            )}

        </div>

    );

}