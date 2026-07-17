type ExternalLinkProps = {
	href: string;
	label: string;
};

function isSafeHttpUrl(href: string): boolean {
	try {
		const url = new URL(href);
		return url.protocol === 'http:' || url.protocol === 'https:';
	} catch {
		return false;
	}
}

export function ExternalLink({ href, label }: ExternalLinkProps) {
	if (!isSafeHttpUrl(href)) {
		return <span>{label}</span>;
	}

	return (
		<a href={href} target="_blank" rel="noreferrer">
			{label}
			<span className="visually-hidden"> (opens in a new tab)</span>
		</a>
	);
}
