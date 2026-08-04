type Props = {
    title: string;
};

export default function SectionTitle({
    title,
}: Props) {

    return (

        <h2 className="text-xl font-bold mb-5">

            {title}

        </h2>

    );

}