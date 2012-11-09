I'm following the example of Zhang, Ackerman, and Adamic in "[Expertise Networks in Online Communities: Structure and Algorithms](http://wwwconference.org/www2007/papers/paper516.pdf)." 

However, I'm using the [Stack Overflow data](http://www.clearbits.net/creators/146-stack-exchange-data-dump), which is *considerably* larger, and also already includes human ratings of most posts. I included a couple of sample files (posts.xml and users.xml) with a; rows of data each, but get the real data (around 7 GB at this time) for real analysis.

This data is not in any format directly useful in R, Gephi, or the like, so this application will transform it into [GraphML](http://graphml.graphdrawing.org/).

You can then read the data in R as follows:

```R
library(igraph)
g = read.graph("./StackOverflow.graphml",format="graphml")
summary(g)
d <- degree(g, mode="in")
dd <- degree.distribution(g, mode="in", cumulative=TRUE)
plot(dd, log="xy", xlab="degree", ylab="cumulative probability", col=1, main="Degree distribution")
```